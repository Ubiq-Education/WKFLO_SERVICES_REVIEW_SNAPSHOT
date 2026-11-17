using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    public partial class SemanticKernelAgentService
    {
        /// <summary>
        /// Runs a image analyzer chat completion agent that analyzes the images sent by the user and provides insights based on the analysis. 
        /// The agent processes the user input message along with any attached image files, generates a response based on the analysis, and streams the
        /// response tokens to the client session.
        /// </summary>
        /// <remarks>The reasoning agent replies with a concise analysis of the user input, omitting
        /// greetings and instructions. Response tokens are streamed incrementally to the client session as they are
        /// generated.</remarks>
        /// <param name="data">The data required to execute the image analyzer agent's operations.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task<string> RunImageAnalyzerChatCompletionAgentAsync(RunImageAnalyzerChatData data, CancellationToken cancellationToken)
        {
            // Create chat completion agent
            var reasoningCompletionAgent = new ChatCompletionAgent()
            {
                Name = "ImageAnalyzer",
                Instructions = $@"Today: {_dateTimeProvider.DateTimeOffsetUtcNow():yyyy-MM-dd} {_dateTimeProvider.DateTimeOffsetUtcNow():HH:mm:ss} UTC.
                    You are an expert assistant specialized in analyzing images.",
                Kernel = data.Kernel,
                Arguments = new KernelArguments(AIProviderHelper.BuildDefaultExecutionSettings(data.Agent))
            };

            var messageContent = new ChatMessageContentItemCollection { new TextContent(data.Message) };

            foreach (var file in data.Files)
            {
                // Bug 8189 / Task 8219: the document's text is already extracted (once) for the main run; this analyzer gets one line about it.
                if (file.Document != null || file.ExtractedText != null)
                {
                    messageContent.Add(new TextContent($"\n\nAttached document: '{file.FileName}' ({file.FileType ?? file.Document?.FileType}). Its content is available to the main assistant."));
                    continue;
                }

                var fileData = file.FileData;
                if (!string.IsNullOrWhiteSpace(fileData))
                {
                    var mimeType = fileData.DetectMimeType();

                    if (mimeType.IsImageMimeType())
                    {
                        var (imageBytes, detectedMimeType) = fileData.ParseImageData();
                        messageContent.Add(new ImageContent(imageBytes, detectedMimeType));
                    }
                    else if (mimeType.IsDocumentMimeType())
                    {
                        var (documentBytes, detectedMimeType, extractedText) = fileData.ParseDocumentData();

                        if (!string.IsNullOrEmpty(extractedText))
                        {
                            var documentPrompt = $"\n\nDocument content ({detectedMimeType}):\n{extractedText}";
                            messageContent.Add(new TextContent(documentPrompt));
                        }
                    }
                }
            }

            var historyForReasonerAgentThread = new ChatHistoryAgentThread(new ChatHistory());
            ChatMessageContent chatUserMessage = new(AuthorRole.User, messageContent);

            var reasoningMessageId = Guid.NewGuid().ToString();
            var reasoningTokensAssistantResponseList = new List<string>();
            await foreach (AgentResponseItem<StreamingChatMessageContent> responseItem in reasoningCompletionAgent.InvokeStreamingAsync(chatUserMessage, historyForReasonerAgentThread, cancellationToken: cancellationToken))
            {
                foreach (var item in responseItem.Message.Items)
                {
#pragma warning disable SKEXP0110
                    switch (item)
                    {
                        case StreamingTextContent text:
                            reasoningTokensAssistantResponseList.Add(text.Text);
                            Console.Write(text.Text);
                            break;
                    }
#pragma warning restore SKEXP0110
                }
            }

            await historyForReasonerAgentThread.DeleteAsync();

            return string.Join(string.Empty, reasoningTokensAssistantResponseList);
        }
    }
}
