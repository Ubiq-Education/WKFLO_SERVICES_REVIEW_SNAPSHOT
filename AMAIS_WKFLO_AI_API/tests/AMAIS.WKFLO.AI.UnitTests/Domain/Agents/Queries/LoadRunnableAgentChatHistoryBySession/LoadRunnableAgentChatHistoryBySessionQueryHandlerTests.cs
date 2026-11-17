using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadRunnableAgentChatHistoryBySession;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.Queries.LoadRunnableAgentChatHistoryBySession
{
    /// <summary>
    /// Covers the fix for conversations persisted before the Anthropic system-role positioning fix:
    /// an AuthorRole.System message anywhere but index 0 must be stripped on load, or resuming an old
    /// conversation immediately fails with Anthropic's "role 'system' must follow a 'user' message..."
    /// error.
    /// </summary>
    public class LoadRunnableAgentChatHistoryBySessionQueryHandlerTests
    {
        private static LoadRunnableAgentChatHistoryBySessionQueryHandler BuildHandler(Mock<IChatStoreService> chatStoreService)
        {
            var tenantContext = new TenantContext { TenantName = "Test" };
            var userContext = new UserContext { Guid = "user-guid" };
            return new LoadRunnableAgentChatHistoryBySessionQueryHandler(
                chatStoreService.Object, tenantContext, userContext, Mock.Of<IDateTimeProvider>());
        }

        [Fact]
        public async Task MidConversationSystemMessages_AreRemoved()
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddMessage(AuthorRole.User, "Hi");
            chatHistory.AddMessage(AuthorRole.Assistant, "Hello!");
            // Legacy mid-conversation system message from before the fix — invalid position.
            chatHistory.AddMessage(AuthorRole.System, "stale reasoning from an old turn");
            chatHistory.AddMessage(AuthorRole.User, "Follow-up question");

            var chatStoreService = new Mock<IChatStoreService>();
            chatStoreService
                .Setup(s => s.LoadChatAsync("Test", 1, "session-1", "user-guid"))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Chat", chatHistory, new Dictionary<string, string>()));

            var handler = BuildHandler(chatStoreService);
            var (_, _, result) = await handler.Handle(new LoadRunnableAgentChatHistoryBySessionQuery
            {
                Agent = new TenantAgent { Id = 1, EnableSTM = true },
                SessionId = "session-1"
            }, TestContext.Current.CancellationToken);

            Assert.Equal(3, result.Count);
            Assert.DoesNotContain(result, m => m.Role == AuthorRole.System);
        }

        [Fact]
        public async Task IndexZeroSystemMessage_IsPreserved()
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddMessage(AuthorRole.System, "pinned background knowledge");
            chatHistory.AddMessage(AuthorRole.User, "Hi");
            chatHistory.AddMessage(AuthorRole.Assistant, "Hello!");

            var chatStoreService = new Mock<IChatStoreService>();
            chatStoreService
                .Setup(s => s.LoadChatAsync("Test", 1, "session-1", "user-guid"))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Chat", chatHistory, new Dictionary<string, string>()));

            var handler = BuildHandler(chatStoreService);
            var (_, _, result) = await handler.Handle(new LoadRunnableAgentChatHistoryBySessionQuery
            {
                Agent = new TenantAgent { Id = 1, EnableSTM = true },
                SessionId = "session-1"
            }, TestContext.Current.CancellationToken);

            Assert.Equal(3, result.Count);
            Assert.Equal(AuthorRole.System, result[0].Role);
        }

        [Fact]
        public async Task ShortTermMemoryDisabled_ReturnsEmptyHistory_WithoutCallingChatStore()
        {
            var chatStoreService = new Mock<IChatStoreService>();
            var handler = BuildHandler(chatStoreService);

            var (_, _, result) = await handler.Handle(new LoadRunnableAgentChatHistoryBySessionQuery
            {
                Agent = new TenantAgent { Id = 1, EnableSTM = false },
                SessionId = "session-1"
            }, TestContext.Current.CancellationToken);

            Assert.Empty(result);
            chatStoreService.Verify(s => s.LoadChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
