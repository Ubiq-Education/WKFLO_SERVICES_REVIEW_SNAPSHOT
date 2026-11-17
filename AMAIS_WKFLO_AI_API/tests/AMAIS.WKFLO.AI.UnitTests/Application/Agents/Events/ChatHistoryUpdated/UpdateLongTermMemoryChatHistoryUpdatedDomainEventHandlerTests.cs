using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Events.ChatHistoryUpdated
{
    public class UpdateLongTermMemoryChatHistoryUpdatedDomainEventHandlerTests
    {
        /// <summary>US 7502 (user decision): a stopped turn adds no content, so long-term memory is not updated.</summary>
        [Fact]
        public async Task Handle_StoppedTurn_DoesNotTouchLongTermMemory()
        {
            var vectorStorageProvider = new Mock<IVectorStorageProvider>(MockBehavior.Strict);
            var handler = new UpdateLongTermMemoryChatHistoryUpdatedDomainEventHandler(
                vectorStorageProvider.Object, new TenantContext { TenantName = "test-tenant" }, new UserContext { Guid = "user-guid-1" });

            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("one");
            chatHistory.AddAssistantMessage("two");
            chatHistory.AddUserMessage("three");
            chatHistory.AddAssistantMessage("four");
            chatHistory.Add(StoppedTurnMarker.Build(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

            await handler.Handle(new ChatHistoryUpdatedDomainEvent(new TenantAgent { Id = 5, EnableLTM = true }, "sess-1", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, chatHistory, Mock.Of<IChatCompletionService>())
            { IsStoppedTurn = true },
                TestContext.Current.CancellationToken);

            vectorStorageProvider.VerifyNoOtherCalls();
        }

        /// <summary>US 7502: on a later, normal turn the "Stopped" divider in the history is never embedded.</summary>
        [Fact]
        public async Task Handle_HistoryWithAStoppedMarker_UpdatesMemoryWithoutTheMarker()
        {
            List<string> embedded = null;
            var vectorStorageProvider = new Mock<IVectorStorageProvider>();
            vectorStorageProvider
                .Setup(x => x.AddAsync("user-guid-1", It.IsAny<IEnumerable<string>>(), "sess-1", 5, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, IEnumerable<string>, string, int, string, string, string, CancellationToken>((_, chunks, _, _, _, _, _, _) => embedded = chunks.ToList())
                .Returns(Task.CompletedTask);
            var handler = new UpdateLongTermMemoryChatHistoryUpdatedDomainEventHandler(
                vectorStorageProvider.Object, new TenantContext { TenantName = "test-tenant" }, new UserContext { Guid = "user-guid-1" });

            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("hello");
            chatHistory.AddAssistantMessage("partial");
            chatHistory.Add(StoppedTurnMarker.Build(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            var notification = new ChatHistoryUpdatedDomainEvent(new TenantAgent { Id = 5, EnableLTM = true }, "sess-1", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, chatHistory, Mock.Of<IChatCompletionService>());

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            Assert.NotNull(embedded);
            Assert.Contains(embedded, c => c.Contains("partial"));
            Assert.DoesNotContain(embedded, c => c.Contains(StoppedTurnMarker.Text));
        }
    }
}
