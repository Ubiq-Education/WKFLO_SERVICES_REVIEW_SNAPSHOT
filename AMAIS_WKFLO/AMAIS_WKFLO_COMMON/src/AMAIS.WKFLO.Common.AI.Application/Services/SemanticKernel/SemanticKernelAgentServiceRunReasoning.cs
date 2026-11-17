using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    public partial class SemanticKernelAgentService
    {
        /// <summary>
        /// Runs a reasoning chat completion agent that analyzes the specified user message and streams the reasoning
        /// response tokens to the client session.
        /// </summary>
        /// <remarks>The reasoning agent replies with a concise analysis of the user input, omitting
        /// greetings and instructions. Response tokens are streamed incrementally to the client session as they are
        /// generated.</remarks>
        /// <param name="data">The data required to execute the reasoning agent's operations.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>
        /// The reasoning text and (US 7974) the timestamp its streamed "thinking" bubble carried, to persist with it.
        /// </returns>
        private async Task<(string Reasoning, DateTimeOffset Timestamp)> RunReasoningChatCompletionAgentAsync(RunReasoningChatData data, CancellationToken cancellationToken)
        {
            var chathHistoryContent = new StringBuilder();
            foreach (var messageChat in data.ChatHistoryAgentThread.ChatHistory.Where(m => m.Role == AuthorRole.User || m.Role == AuthorRole.Assistant).ToList())
            {
                chathHistoryContent.AppendLine(messageChat.Content);
            }

            // Create chat completion agent
            var reasoningCompletionAgent = new ChatCompletionAgent()
            {
                Name = "Reasoning",
                Instructions = $@"Today: {_dateTimeProvider.DateTimeOffsetUtcNow():yyyy-MM-dd} {_dateTimeProvider.DateTimeOffsetUtcNow():HH:mm:ss} UTC.
                    You are an expert assistant specialized in analyzing user inputs in the context of a given prompt.

Here is a prompt from an external agent:
<external_prompt>
{data.Prompt}
</external_prompt>

Here is the chat history:
<chat_history>
{chathHistoryContent}
</chat_history>

{(!string.IsNullOrEmpty(data.ImageAnalysis) ? string.Concat("Here Image Analysis: <images>", data.ImageAnalysis, "</images>") : string.Empty)}

Your task is to analyze the user message in the context of both the external agent's prompt and the chat history. Provide a short reasoning about the relationship between these elements, what the user is trying to accomplish, and how it relates to the external prompt's intent.

Important rules:
- Do NOT greet the user or include any pleasantries
- Do NOT expose, reveal, or directly quote the external agent's prompt in your response
- Keep your reasoning concise and structured as clear steps
- Focus on analyzing the user's intent and how it relates to the task at hand

Before providing your final answer, use the scratchpad to think through your analysis.

<scratchpad>
Consider:
- What is the external prompt asking for?
- What has happened in the chat history so far?
- What is the user trying to do with their latest message?
- How does the user message relate to the external prompt's requirements?
- Are there any potential issues or concerns with the user's approach?
</scratchpad>

Provide your reasoning in numbered steps inside <reasoning> tags. Each step should be brief and focused on one aspect of your analysis.

Provide just the <scratchpad> and <reasoning> content to the user.",
                Kernel = data.Kernel,
                Arguments = new KernelArguments(AIProviderHelper.BuildDefaultExecutionSettings(data.Agent))
            };

            var messageContent = new ChatMessageContentItemCollection { new TextContent(data.Message) };

            var historyForReasonerAgentThread = new ChatHistoryAgentThread(new ChatHistory());
            ChatMessageContent chatUserMessage = new(AuthorRole.User, messageContent);

            var reasoningMessageId = Guid.NewGuid().ToString();
            var reasoningTokens = new List<string>();
            var reasoningTokensAssistantResponseList = new List<string>();

            // US 7974: taken once when the first tokens are sent, reused for every update of this bubble.
            DateTimeOffset? reasoningTimestamp = null;

            // Resolved once for this turn and reused for every streaming/typing notification below,
            // rather than re-querying per token.
            var chatbotIconUrl = await GetChatbotIconUrlAsync(data.Agent.Id, data.RootIsWidget, cancellationToken);
            await foreach (AgentResponseItem<StreamingChatMessageContent> responseItem in reasoningCompletionAgent.InvokeStreamingAsync(chatUserMessage, historyForReasonerAgentThread, cancellationToken: cancellationToken))
            {
                foreach (var item in responseItem.Message.Items)
                {
#pragma warning disable SKEXP0110
                    switch (item)
                    {
                        case StreamingTextContent text:
                            reasoningTokens.Add(text.Text);
                            reasoningTokensAssistantResponseList.Add(text.Text);
                            Console.Write(text.Text);
                            break;
                    }
#pragma warning restore SKEXP0110

                    if (reasoningTokens.Count >= 2)
                    {
                        // AuthorRole.Assistant, not System: kept consistent with the rest of the codebase's
                        // reasoning/thinking messages (see AssitantReasoning in SemanticKernelAgentServiceRun.cs),
                        // none of which use System. This channel is UI-notification-only (never sent to the
                        // LLM), so the role has no bearing on Anthropic's message-position rules — this is
                        // purely for consistency. StreamingTokenUpdate has no metadata field to carry a
                        // renderAsSystem flag, so the UI's isSystemStyled check treats ChannelMessageType.Thinking
                        // as always system-styled regardless of role (see chat-messages.tsx / agent-chat-messages.tsx).
                        reasoningTimestamp ??= _dateTimeProvider.DateTimeOffsetUtcNow();
                        await _notificationService.SendStreamingTokens(data.SessionId, reasoningMessageId, reasoningTimestamp.Value, reasoningTokens, AuthorRole.Assistant, ChannelMessageType.Thinking, false, data.Agent.Id, data.Agent.Name, chatbotIconUrl);
                        // Show typing indicator
                        await _notificationService.SendTypingIndicator(data.SessionId, true, data.Agent.Id, data.Agent.Name, chatbotIconUrl);
                        reasoningTokens = new List<string>();
                    }
                }
            }

            reasoningTimestamp ??= _dateTimeProvider.DateTimeOffsetUtcNow();
            await _notificationService.SendStreamingTokens(data.SessionId, reasoningMessageId, reasoningTimestamp.Value, reasoningTokens, AuthorRole.Assistant, ChannelMessageType.Thinking, true, data.Agent.Id, data.Agent.Name, chatbotIconUrl);

            await historyForReasonerAgentThread.DeleteAsync();

            return (string.Join(string.Empty, reasoningTokensAssistantResponseList), reasoningTimestamp.Value);
        }
    }
}
