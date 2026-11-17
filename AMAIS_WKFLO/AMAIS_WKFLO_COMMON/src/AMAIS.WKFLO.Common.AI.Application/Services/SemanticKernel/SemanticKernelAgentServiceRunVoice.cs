using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.SearchRunnableAgentVectorDataByEmail;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.AI;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using System.Text.RegularExpressions;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    public partial class SemanticKernelAgentService
    {
        private static readonly Regex HtmlBlockBoundary = new(@"<\s*(p|li|div)[^>]*>.*?</\s*\1\s*>", RegexOptions.Compiled);


        /// <summary>
        /// Runs a chat completion agent to process a user message within a chat session and returns the assistant's
        /// response along with token usage details.
        /// </summary>
        /// <remarks>This method streams the assistant's response tokens to the notification service as
        /// they are generated. The chat history is updated with both user and assistant messages, including optional
        /// reasoning if provided. The chat thread is deleted after processing is complete. Exceptions may be thrown if
        /// required parameters are null or if the operation is cancelled.</remarks>
        /// <param name="data">The data required to execute the voice chat completion agent's operations.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an AgentAnswer object with the
        /// assistant's response message, a unique message identifier, and token usage statistics.</returns>
        internal async Task<AgentAnswer> RunVoiceChatCompletionAgentAsync(RunVoiceChatData data, CancellationToken cancellationToken)
        {
            _chatContext.CurrentChatHistory = data.ChatHistoryAgentThread.ChatHistory;

            // If LTM is enabled, search for relevant information
            var vectorialResults = await _mediator.Send(new SearchRunnableAgentVectorDataByEmailQuery() { Agent = data.Agent, Message = data.Message }, cancellationToken);
            if (vectorialResults != null && vectorialResults.Any())
            {
                data.Prompt += "Relevant information about the user. Please take these next statments into account when replying back to the user:\n";
                foreach (var result in vectorialResults)
                {
                    data.Prompt += $"- {result}\n";
                }
            }

            // Inject PriorityQA predefined answers into the prompt so they are always honoured
            // regardless of FunctionChoiceBehavior or topic restrictions in the agent's system prompt
            var priorityQAInjection = BuildPriorityQAInjection(data.Agent, data.ToolPermissionPolicy);
            if (!string.IsNullOrEmpty(priorityQAInjection))
                data.Prompt += priorityQAInjection;

            // Inject ContactForm tool instructions so the AI knows exactly when to call the function
            var contactFormInjection = await BuildContactFormInjectionAsync(data.Agent, _tenantContext.TenantName, _chatContext.SubjectId, data.ToolPermissionPolicy);
            if (!string.IsNullOrEmpty(contactFormInjection))
                data.Prompt += contactFormInjection;

            // Hand-off prompt: catalog of allowlisted agents plus, on an @mention, a forced call. Returns the prompt
            // and the target names (unused in voice: no missed hand-off warning there).
            var (agentEventPublisherInjection, _) = await BuildAgentEventPublisherInjectionAsync(data.Agent, data.Message);
            if (!string.IsNullOrEmpty(agentEventPublisherInjection))
                data.Prompt += agentEventPublisherInjection;

            // Only build the Amais CMS page/feature integration context line when Amais skills are enabled
            // for this run — otherwise the model would be instructed to call Amais-only tools that aren't
            // actually available.
            var amaisContextLine = data.AmaisSkillsEnabled ? BuildAmaisContextLine(_chatContext, IsPageAnalyticsAvailable(_chatContext)) : string.Empty;

            // Inject the agent's actual tools/plugins so capability questions ("what can you help with?")
            // are answered from real configured tools instead of the generic system-prompt persona text.
            // Only built when Amais skills are enabled for this run — same gate as amaisContextLine above.
            var capabilitiesInjection = data.AmaisSkillsEnabled ? BuildAmaisAgentCapabilitiesInjection(data.Agent, _chatContext, data.ToolPermissionPolicy) : string.Empty;
            if (!string.IsNullOrEmpty(capabilitiesInjection))
                data.Prompt += capabilitiesInjection;

            // Get chat completion service
            var chatCompletionService = data.Kernel.GetRequiredService<IChatCompletionService>();

            // Sanitize agent name to comply with ChatCompletionAgent requirements
            var sanitizedAgentName = data.Agent.Name.SanitizeForAgentName();

            // Bug 8189 / Task 8219: the passages of the chat's large documents that match what was said, for this turn only (never in the history).
            var attachedDocumentsContext = await BuildAttachedDocumentContextAsync(data.ChatHistoryAgentThread.ChatHistory, [], data.SessionId, data.Message, false, data.Kernel, cancellationToken);

#pragma warning disable SKEXP0110
            // Create chat completion agent
            var chatCompletionAgent = new ChatCompletionAgent()
            {
                Name = sanitizedAgentName,
                Instructions = BuildChatCompletionAgentInstructions(_dateTimeProvider.DateTimeOffsetUtcNow(), data.Prompt, amaisContextLine, data.AssitantReasoning, data.IsMaisyAgent, attachedDocumentsContext),
                Kernel = data.Kernel,
                Arguments = new KernelArguments(AIProviderHelper.BuildDefaultExecutionSettings(data.Agent)),
                HistoryReducer = new SafeChatHistorySummarizationReducer(chatCompletionService, 20, 30)
            };
#pragma warning restore SKEXP0110

            // Token usage tracking
            long inputTokensCount = 0;
            long outputTokensCount = 0;

            // Reduce prior to invoking the agent
#pragma warning disable SKEXP0110
            await chatCompletionAgent.ReduceAsync(data.ChatHistoryAgentThread.ChatHistory, default);
#pragma warning restore SKEXP0110 

            // Message Id
            var messageId = Guid.NewGuid().ToString();

            // Resolved once for this turn and reused for every streaming/typing notification below,
            // rather than re-querying per token.
            var chatbotIconUrl = await GetChatbotIconUrlAsync(data.Agent.Id, data.RootIsWidget, cancellationToken);
            _chatTurnErrorReporter?.SetCurrentAgent(new ChatTurnAgent(data.Agent.Id, data.Agent.Name, chatbotIconUrl));

            // Add new user message
            var metadataText = new Dictionary<string, object>
            {
                { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() },
                { CommonMetadataConsts.MessageId, data.UserMessageId.ToString() }
            };

            if (!string.IsNullOrEmpty(data.TopicName))
                metadataText[CommonMetadataConsts.TopicName] = data.TopicName;

            var messageContentText = new ChatMessageContentItemCollection { new TextContent(data.Message) };

            data.ChatHistoryAgentThread.ChatHistory.AddMessage(AuthorRole.User, messageContentText, metadata: metadataText);

            if (!string.IsNullOrWhiteSpace(data.TopicHandoffMessage))
            {
                var handoffInstruction = $"If you are unable to provide the information the user needs, or if the user asks to contact someone, use the following contact information: {data.TopicHandoffMessage}";
                SemanticKernelExtensions.UpsertSystemMessageSection(data.ChatHistoryAgentThread.ChatHistory, "TopicHandoff", handoffInstruction, null);
            }

            // Invoke agent with user message and chat history
            var tokens = new List<string>();
            var fullTextBuilder = new StringBuilder();

            // Stream LLM and accumulate into sentences for TTS
            var sentenceBuffer = new StringBuilder();
            var streamingFailed = false;

            // US 7974: the answer's timestamp, taken once when its first tokens are sent and reused for every
            // streaming update and for the persisted message, so live = reloaded.
            DateTimeOffset? messageTimestamp = null;
            var historyCountBeforeStreaming = data.ChatHistoryAgentThread.ChatHistory.Count;

            try
            {
                await foreach (AgentResponseItem<StreamingChatMessageContent> responseItem in chatCompletionAgent.InvokeStreamingAsync(data.ChatHistoryAgentThread))
                {
                    responseItem.ProcessResponse(_dateTimeProvider, ref inputTokensCount, ref outputTokensCount);
                    foreach (var item in responseItem.Message.Items)
                    {
#pragma warning disable SKEXP0110
                        switch (item)
                        {
                            case StreamingTextContent text:
                                _logger.LogInformation("Streaming token: {Token}", text.Text);
                                tokens.Add(text.Text);
                                fullTextBuilder.Append(text.Text);

                                sentenceBuffer.Append(text.Text);
                                // Check if we have a complete sentence
                                var bufferText = sentenceBuffer.ToString();
                                var match = HtmlBlockBoundary.Match(bufferText);
                                while (match.Success)
                                {
                                    var sentence = bufferText[..(match.Index + match.Length)].Trim();
                                    sentenceBuffer.Clear();
                                    bufferText = bufferText[(match.Index + match.Length)..];
                                    sentenceBuffer.Append(bufferText);

                                    if (!string.IsNullOrWhiteSpace(sentence))
                                    {
                                        await SynthesizeAndSendAsync(sentence, data, chatbotIconUrl, isFinal: false, cancellationToken);
                                    }

                                    match = HtmlBlockBoundary.Match(sentenceBuffer.ToString());
                                }
                                break;

                            case StreamingReasoningContent reasoning:
                                _logger.LogInformation("Streaming reasoning token: {Token} with metadata: {Metadata}", reasoning.Text, string.Join(", ", reasoning.Metadata?.Select(a => $"{a.Key}={a.Value}") ?? new List<string>()));
                                break;

                            case StreamingFunctionCallUpdateContent tool:
                                _logger.LogInformation("Streaming tool call update: {ToolName} with metadata: {Metadata}", tool.Name, string.Join(", ", tool.Metadata?.Select(a => $"{a.Key}={a.Value}") ?? new List<string>()));
                                break;
                        }
#pragma warning restore SKEXP0110

                        if (tokens.Count >= 5)
                        {
                            messageTimestamp ??= _dateTimeProvider.DateTimeOffsetUtcNow();
                            await _notificationService.SendStreamingTokens(data.SessionId, messageId, messageTimestamp.Value, ReorganizeTokens(tokens), AuthorRole.Assistant, ChannelMessageType.Normal, false, data.Agent.Id, data.Agent.Name, chatbotIconUrl);
                            // Show typing indicator
                            await _notificationService.SendTypingIndicator(data.SessionId, true, data.Agent.Id, data.Agent.Name, chatbotIconUrl);
                            tokens.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Any failure during streaming (provider rejection, network error, malformed request, a
                // failed speech synthesis, etc.). US 7959 / Task 7961: recorded as a code (the exception only
                // goes to the logs; a speech failure already recorded its own code) instead of sending a
                // code-less bubble here; the turn's choke point reports it to the chat once. A Stop is no error.
                _logger.LogError(ex, "Streaming voice chat completion failed for session {SessionId}", data.SessionId);
                RecordTurnFailure(ex, data.Agent, chatbotIconUrl, cancellationToken.IsCancellationRequested);
                streamingFailed = true;

                // Discard any partial content streamed before the failure — the error is reported as a
                // separate message, so there is no partial answer/narration left to finalize.
                tokens.Clear();
                fullTextBuilder.Clear();
                sentenceBuffer.Clear();
            }

            // Flush any remaining buffer as the last sentence
            var remaining = sentenceBuffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                await SynthesizeAndSendAsync(remaining, data, chatbotIconUrl, isFinal: true, cancellationToken);
            }
            else
            {
                await _notificationService.SendAudioChunkAsync(data.SessionId, string.Empty, string.Empty, true);
            }

            messageTimestamp ??= _dateTimeProvider.DateTimeOffsetUtcNow();
            await _notificationService.SendStreamingTokens(data.SessionId, messageId, messageTimestamp.Value, ReorganizeTokens(tokens), AuthorRole.Assistant, ChannelMessageType.Normal, true, data.Agent.Id, data.Agent.Name, chatbotIconUrl);
            tokens.Clear();

            // Show typing indicator. US 7959 / Task 7961: not for a failed run - its agent's bubble must stay off.
            if (!streamingFailed)
                await _notificationService.SendTypingIndicator(data.SessionId, true, data.Agent.Id, data.Agent.Name, chatbotIconUrl);

            // Combine assistant responses
            var fullText = fullTextBuilder.ToString();

            StampStreamedReply(data.ChatHistoryAgentThread.ChatHistory, historyCountBeforeStreaming, fullText, messageId, messageTimestamp.Value);

            // US 7142 corner case: tag this turn's own assistant reply as a ContactForm filler - not a
            // real answer - whenever the ContactForm tool actually fired this turn. Same rationale/
            // mechanism as SemanticKernelAgentServiceRun.RunChatCompletionAgentAsync's identical block -
            // voice shares the same kernel tools (including ContactFormPlugin) via AddWkflowToolsAsync.
            if (_chatContext.ContactFormRequestedThisTurn && data.ChatHistoryAgentThread.ChatHistory.Count > 0)
            {
                var lastAssistantMessage = data.ChatHistoryAgentThread.ChatHistory.Last();
                if (lastAssistantMessage.Role == AuthorRole.Assistant)
                {
                    var fillerMetadata = new Dictionary<string, object>(lastAssistantMessage.Metadata ?? new Dictionary<string, object>())
                    {
                        [CommonMetadataConsts.IsContactFormFiller] = true
                    };

                    data.ChatHistoryAgentThread.ChatHistory.RemoveAt(data.ChatHistoryAgentThread.ChatHistory.Count - 1);
                    data.ChatHistoryAgentThread.ChatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, lastAssistantMessage.Content, metadata: fillerMetadata));
                }
            }

            // Process assistant post messages that other part of the system like plugin could have added
            var postMessagesSent = _chatContext.AssistantPostMessagesToBeProcessed.Count > 0;
            await ProcessAssistantPostMessagesAsync(data.ChatHistoryAgentThread.ChatHistory, data.Agent, data.RootIsWidget);

            // US 7959 / Task 7961: the user now has the whole answer; a later failure is not reported as a failed turn.
            if (!streamingFailed && (!string.IsNullOrWhiteSpace(fullText) || postMessagesSent))
                _chatTurnFailureState.MarkAnswerDelivered();

            // Create AgentAnswer with token usage and other details

            var agentAnswer = new AgentAnswer
            {
                MessageId = messageId,
                Message = fullText,
                TokenUsage = new AITokenUsage
                {
                    InputTokenCount = inputTokensCount,
                    OutputTokenCount = outputTokensCount
                },
                Provider = data.Agent.AIModel.Provider,
                Model = data.Agent.AIModel.Model,
                AgentType = data.AgentTagNames != null && data.AgentTagNames.Any() ? string.Join("_", data.AgentTagNames) : data.Agent.Name.Replace(' ', '_'),
                AgentTags = data.AgentTagNames,
                AgentId = data.Agent.Id,
                AIModelId = data.Agent.AIModel.Id
            };

            await _mediator.Publish(new AgentTokensUsedDomainEvent(agentAnswer), cancellationToken);

            // Publish chat history updated event for RunnableAgents to trigger any updates in the UI or other parts of the system
            if (data.Agent is RunnableAgent runnableAgent)
            {
                // US 7078: never persist ephemeral sibling-agent context — see BuildPersistableChatHistory.
                await _mediator.Publish(new ChatHistoryUpdatedDomainEvent
                (
                    runnableAgent,
                    data.SessionId,
                    data.ChatName,
                    data.CreatedAt,
                    _dateTimeProvider.DateTimeOffsetUtcNow(),
                    BuildPersistableChatHistory(data.ChatHistoryAgentThread.ChatHistory, data.DetachedStoppedMarkers),
                    chatCompletionService
                ), cancellationToken);
            }

            // US 7142 corner case: when this turn's response is just the ContactForm tool's
            // registration-required filler, record this question's MessageId so the eventual replay turn
            // can reuse it - see SemanticKernelAgentServiceRun.RunChatCompletionAgentAsync's identical
            // block for the full rationale. Best-effort, same "never break the main chat flow" guarantee
            // as the resolution scheduling below.
            if (_chatContext.ContactFormRequestedThisTurn && _contactFormReplayStateService != null)
            {
                try
                {
                    await _contactFormReplayStateService.RecordDeferredQuestionAsync(data.SessionId, data.UserMessageId, cancellationToken);
                }
                catch (Exception)
                {
                    // Swallow - see comment above.
                }
            }

            // Schedule the near-immediate, message-level resolution judgment (US 7142) - same mechanism
            // and guards as SemanticKernelAgentServiceRun.RunChatCompletionAgentAsync's identical block,
            // now also covering voice-triggered turns (a Copilot PR review found voice was never wired
            // into classification/resolution at all - see that method's own comment for the full
            // rationale on the ContactForm and blank-answer guards below).
            if (_messageResolutionWorkflowScheduler != null
                && !_chatContext.ContactFormRequestedThisTurn
                && !string.IsNullOrWhiteSpace(fullText))
            {
                try
                {
                    var instanceId = $"msg-resolution-{data.UserMessageId}";
                    var resolutionInput = new MessageResolutionWorkflowInput
                    {
                        TenantName = _tenantContext.TenantName,
                        AgentId = data.Agent.Id,
                        SessionId = data.SessionId,
                        UserId = _userContext.Guid,
                        MessageId = data.UserMessageId,
                        Question = data.Message,
                        Answer = fullText,
                        AnswerMessageId = Guid.Parse(messageId)
                    };

                    await _messageResolutionWorkflowScheduler.ScheduleAsync(instanceId, resolutionInput);
                }
                catch (Exception)
                {
                    // Swallow - resolution scheduling failure must not break the main chat flow.
                }
            }

            // Delete the thread when it is no longer needed
            await data.ChatHistoryAgentThread.DeleteAsync();

            return agentAnswer;
        }

        private async Task SynthesizeAndSendAsync(string sentence, RunVoiceChatData data, string chatbotIconUrl, bool isFinal, CancellationToken cancellationToken)
        {
            try
            {
                sentence = HtmlToPlainText(sentence);
                var audioBytes = await _azureSpeechService.SynthesizeSpeechAsync(sentence, data.Voice, data.Language, cancellationToken);
                var base64Audio = Convert.ToBase64String(audioBytes);
                await _notificationService.SendAudioChunkAsync(data.SessionId, base64Audio, sentence, isFinal);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // US 7959 / Task 7961: the exception text no longer reaches the chat (it used to travel in a
                // ConversationStateUpdate "error"). Recorded as ChatVoiceSynthesisFailed: terminal unless the text answer
                // was already delivered. The turn's choke point reports it once.
                _logger.LogError(ex, "TTS synthesis failed for sentence in session {SessionId}", data.SessionId);
                _chatTurnFailureState.Record(
                    ChatTurnFailure.Create(ChatErrorCodes.ChatVoiceSynthesisFailed, data.Agent?.Id, data.Agent?.Name, chatbotIconUrl,
                        isTerminal: !_chatTurnFailureState.AnswerDelivered),
                    ex);
                throw;
            }
        }

        public static string HtmlToPlainText(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return HtmlEntity.DeEntitize(doc.DocumentNode.InnerText);
        }
    }
}
