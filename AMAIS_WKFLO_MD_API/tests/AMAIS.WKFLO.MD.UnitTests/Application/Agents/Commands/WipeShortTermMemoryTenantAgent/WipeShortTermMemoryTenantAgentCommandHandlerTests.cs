using AMAIS.WKFLO.Common.Models.Documents;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.WipeShortTermMemoryTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.WipeShortTermMemoryTenantAgent
{
    public class WipeShortTermMemoryTenantAgentCommandHandlerTests : IClassFixture<WipeShortTermMemoryTenantAgentCommandHandlerTesting>
    {
        private readonly WipeShortTermMemoryTenantAgentCommandHandlerTesting _testing;

        public WipeShortTermMemoryTenantAgentCommandHandlerTests(WipeShortTermMemoryTenantAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(999, 0)]
        public async Task Handle_ReturnsOk_WithExpectedDeletedConversationCount(int agentId, int expectedDeletedConversations)
        {
            _testing.Reset();

            // Act
            var result = await _testing.Handler.Handle(new WipeShortTermMemoryTenantAgentCommand { Id = agentId }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<TenantAgentWipeShortTermMemoryResult> success)
            {
                Assert.Equal(agentId, success.Data.AgentId);
                Assert.Equal(expectedDeletedConversations, success.Data.DeletedConversations);
            }
            else
            {
                Assert.Fail("Expected SuccessResult<TenantAgentWipeShortTermMemoryResult>");
            }
        }

        [Fact]
        public async Task Handle_DeletesTheDocumentsOfEveryWipedChat_InThatChatsOwnScope()
        {
            _testing.Reset();

            await _testing.Handler.Handle(new WipeShortTermMemoryTenantAgentCommand { Id = _testing.ActiveAgentId }, TestContext.Current.CancellationToken);

            // Agent-scoped: another agent that ran in the same session (a hand-off) keeps its documents.
            _testing.MockChatDocumentStore.Verify(x => x.DeleteAgentDocumentsAsync(new ChatDocumentScope("Test", "session-1", "user-1"),
                It.Is<IReadOnlyCollection<int>>(a => a.Count == 1 && a.Contains(_testing.ActiveAgentId)), It.IsAny<CancellationToken>()), Times.Once);
            _testing.MockChatDocumentStore.Verify(x => x.DeleteAgentDocumentsAsync(new ChatDocumentScope("Test", "session-2", "user-2"),
                It.Is<IReadOnlyCollection<int>>(a => a.Count == 1 && a.Contains(_testing.ActiveAgentId)), It.IsAny<CancellationToken>()), Times.Once);
            _testing.MockChatDocumentStore.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Handle_WhenDeletingDocumentsFails_TheChatsAreStillWiped()
        {
            _testing.Reset();
            _testing.MockChatDocumentStore
                .Setup(x => x.DeleteAgentDocumentsAsync(It.IsAny<ChatDocumentScope>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("database down"));

            var result = await _testing.Handler.Handle(new WipeShortTermMemoryTenantAgentCommand { Id = _testing.ActiveAgentId }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantAgentWipeShortTermMemoryResult>>(result);
            Assert.Equal(2, success.Data.DeletedConversations);
        }

        [Fact]
        public async Task Handle_WithPublishedAgent_AlsoDeletesPublishedAgentConversations()
        {
            _testing.SetupWithPublishedAgent();

            // Act
            var result = await _testing.Handler.Handle(new WipeShortTermMemoryTenantAgentCommand { Id = _testing.ActiveAgentId }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<TenantAgentWipeShortTermMemoryResult> success)
            {
                // 2 TenantAgent conversations + 1 PublishedTenantAgent conversation
                Assert.Equal(3, success.Data.DeletedConversations);
            }
            else
            {
                Assert.Fail("Expected SuccessResult<TenantAgentWipeShortTermMemoryResult>");
            }
        }

        [Fact]
        public async Task Handle_WithPublishedAgent_CallsDeleteChatWithCorrectAgentId()
        {
            _testing.SetupWithPublishedAgent();

            // Act
            await _testing.Handler.Handle(new WipeShortTermMemoryTenantAgentCommand { Id = _testing.ActiveAgentId }, TestContext.Current.CancellationToken);

            // Assert: blob deletion uses conversation.AgentId, not the command Id
            _testing.MockChatStoreService.Verify(
                x => x.DeleteChatAsync(_testing.TenantName, _testing.ActiveAgentId, It.IsAny<string>(), It.IsAny<string>()),
                Times.Exactly(2));
            _testing.MockChatStoreService.Verify(
                x => x.DeleteChatAsync(_testing.TenantName, _testing.PublishedAgentId, It.IsAny<string>(), It.IsAny<string>()),
                Times.Once);
        }
    }
}
