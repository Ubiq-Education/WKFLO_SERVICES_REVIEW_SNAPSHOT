using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Covers SafeChatHistorySummarizationReducer, the Option-B replacement for SK's
    /// ChatHistorySummarizationReducer: it sends its internal summarization instruction as an
    /// AuthorRole.User message instead of AuthorRole.System, since SK's built-in reducer appends
    /// that instruction unconditionally as a trailing System message, which Anthropic rejects
    /// unless it immediately follows a User message (or a server-tool-result-ending Assistant turn)
    /// and only on a handful of models - Sonnet and others 400 on it. It also fixes an SK off-by-one
    /// (unclamped messageIndex = chatHistory.Count - targetCount can equal chatHistory.Count when a
    /// pinned system message drives the effective targetCount to 0, indexing one past the end).
    /// </summary>
    public class SafeChatHistorySummarizationReducerTests
    {
        private static Mock<IChatCompletionService> BuildChatCompletionServiceMock(out List<ChatHistory> capturedRequests)
        {
            var captured = new List<ChatHistory>();
            var mock = new Mock<IChatCompletionService>();
            mock.Setup(x => x.GetChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ChatHistory, PromptExecutionSettings, Kernel, CancellationToken>((history, _, _, _) => captured.Add(history))
                .ReturnsAsync((IReadOnlyList<ChatMessageContent>)[new ChatMessageContent(AuthorRole.Assistant, "summary text")]);
            capturedRequests = captured;
            return mock;
        }

        [Fact]
        public async Task ReduceAsync_SendsSummarizationInstructionAsUserMessage_NeverSystem()
        {
            var chatCompletionServiceMock = BuildChatCompletionServiceMock(out var capturedRequests);

            var chatHistory = new ChatHistory();
            chatHistory.AddMessage(AuthorRole.User, "Hi");
            chatHistory.AddMessage(AuthorRole.Assistant, "Hello!");
            chatHistory.AddMessage(AuthorRole.User, "How are you?");
            chatHistory.AddMessage(AuthorRole.Assistant, "Doing well.");

            var reducer = new SafeChatHistorySummarizationReducer(chatCompletionServiceMock.Object, 1);

            var result = await reducer.ReduceAsync(chatHistory, CancellationToken.None);

            Assert.NotNull(result);
            var summarizationRequest = Assert.Single(capturedRequests);
            Assert.DoesNotContain(summarizationRequest, m => m.Role == AuthorRole.System);
            Assert.Equal(AuthorRole.User, summarizationRequest[^1].Role);
            Assert.Contains("summarization-instructions", summarizationRequest[^1].Content);
        }

        [Fact]
        public async Task ReduceAsync_PinnedSystemMessageWithTargetCountOne_DoesNotThrow()
        {
            var chatCompletionServiceMock = BuildChatCompletionServiceMock(out _);

            // Same shape that throws ArgumentOutOfRangeException in stock SK's
            // ChatHistorySummarizationReducer: a system message pinned at index 0 combined with
            // targetCount:1 drives the effective target to 0, and SK indexes chatHistory[chatHistory.Count].
            var chatHistory = new ChatHistory();
            chatHistory.AddMessage(AuthorRole.System, "pinned background");
            chatHistory.AddMessage(AuthorRole.User, "Hi");
            chatHistory.AddMessage(AuthorRole.Assistant, "Hello!");
            chatHistory.AddMessage(AuthorRole.User, "How are you?");
            chatHistory.AddMessage(AuthorRole.Assistant, "Doing well.");

            var reducer = new SafeChatHistorySummarizationReducer(chatCompletionServiceMock.Object, 1);

            var result = await reducer.ReduceAsync(chatHistory, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(AuthorRole.System, result.First().Role);
        }

        [Fact]
        public async Task ReduceAsync_HistoryTooShortToTruncate_ReturnsNullWithoutCallingService()
        {
            var chatCompletionServiceMock = BuildChatCompletionServiceMock(out var capturedRequests);

            var chatHistory = new ChatHistory();
            chatHistory.AddMessage(AuthorRole.User, "Hi");
            chatHistory.AddMessage(AuthorRole.Assistant, "Hello!");

            var reducer = new SafeChatHistorySummarizationReducer(chatCompletionServiceMock.Object, 10, 10);

            var result = await reducer.ReduceAsync(chatHistory, CancellationToken.None);

            Assert.Null(result);
            Assert.Empty(capturedRequests);
        }
    }
}
