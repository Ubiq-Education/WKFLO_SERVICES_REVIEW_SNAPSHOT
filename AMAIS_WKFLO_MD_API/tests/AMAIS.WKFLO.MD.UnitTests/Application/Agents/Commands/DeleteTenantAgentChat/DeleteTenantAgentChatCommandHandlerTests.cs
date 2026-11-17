using AMAIS.WKFLO.Common.Models.Documents;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgentChat;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.DeleteTenantAgentChat
{
    public class DeleteTenantAgentChatCommandHandlerTests : IClassFixture<DeleteTenantAgentChatCommandHandlerTesting>
    {
        private readonly DeleteTenantAgentChatCommandHandlerTesting _testing;

        public DeleteTenantAgentChatCommandHandlerTests(DeleteTenantAgentChatCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_DeletesTheChatsDocuments_InTheChatsOwnTenantSessionAndUserScope()
        {
            _testing.Initialize(chatExists: true);

            var result = await _testing.Handler.Handle(new DeleteTenantAgentChatCommand { Id = 1, SessionId = "session-1" }, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Ok, result.ResultType);
            // Agent-scoped: the chat of agent 1 only; other agents of a hand-off in the same session keep their documents.
            _testing.MockChatDocumentStore.Verify(x => x.DeleteAgentDocumentsAsync(new ChatDocumentScope("Test", "session-1", _testing.UserGuid),
                It.Is<IReadOnlyCollection<int>>(a => a.Count == 1 && a.Contains(1)), It.IsAny<CancellationToken>()), Times.Once);
            _testing.MockChatDocumentStore.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Handle_ChatNotFound_DeletesNoDocuments()
        {
            _testing.Initialize(chatExists: false);

            var result = await _testing.Handler.Handle(new DeleteTenantAgentChatCommand { Id = 1, SessionId = "unknown" }, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.NotFound, result.ResultType);
            _testing.MockChatDocumentStore.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Handle_WhenDeletingDocumentsFails_TheChatIsStillDeleted()
        {
            _testing.Initialize(chatExists: true);
            _testing.MockChatDocumentStore
                .Setup(x => x.DeleteAgentDocumentsAsync(It.IsAny<ChatDocumentScope>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("database down"));

            var result = await _testing.Handler.Handle(new DeleteTenantAgentChatCommand { Id = 1, SessionId = "session-1" }, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Ok, result.ResultType);
        }
    }
}
