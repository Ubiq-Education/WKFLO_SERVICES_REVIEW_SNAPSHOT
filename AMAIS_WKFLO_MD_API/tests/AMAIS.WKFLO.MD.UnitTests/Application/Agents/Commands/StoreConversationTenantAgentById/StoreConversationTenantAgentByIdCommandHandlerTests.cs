using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.StoreConversationTenantAgentById;
using AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.StoreConversationTenantAgentById
{
    public class StoreConversationTenantAgentByIdCommandHandlerTests : IClassFixture<StoreConversationTenantAgentByIdCommandHandlerTesting>
    {
        private readonly StoreConversationTenantAgentByIdCommandHandlerTesting _testing;

        public StoreConversationTenantAgentByIdCommandHandlerTests(StoreConversationTenantAgentByIdCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("session-1", "user", "Hello")]
        [InlineData("session-2", "assistant", "How can I help you?")]
        public async Task Handle_WithValidPayload_ReturnsOk(string sessionId, string role, string text)
        {
            _testing.Reset();

            // Act
            var result = await _testing.Handler.Handle(BuildCommand(_testing.ActiveAgentId, sessionId, role, text), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<bool> success)
                Assert.True(success.Data);
        }

        [Fact]
        public async Task Handle_WithNonExistentAgent_ReturnsNotFound()
        {
            _testing.Reset();
            _testing.SetupNonExistentAgent();

            // Act
            var result = await _testing.Handler.Handle(BuildCommand(999, "session-x", "user", "Hello"), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.NotFound, result.ResultType);
        }

        [Fact]
        public async Task Handle_WithValidPayload_CallsSaveChatOnce()
        {
            _testing.Reset();
            var sessionId = "session-idempotent";

            // Act
            await _testing.Handler.Handle(BuildCommand(_testing.ActiveAgentId, sessionId, "user", "First message"), TestContext.Current.CancellationToken);

            // Assert
            _testing.VerifySaveChatCalledOnce(sessionId);
        }

        [Fact]
        public async Task Handle_WithValidPayload_SyncsConversationWithSubjectIdFromChatContext()
        {
            _testing.Reset();
            var sessionId = "session-subject-id";

            // Act
            await _testing.Handler.Handle(BuildCommand(_testing.ActiveAgentId, sessionId, "user", "Hello"), TestContext.Current.CancellationToken);

            // Assert
            _testing.MockMediator.Verify(x => x.Send(
                It.Is<SyncConversationCommand>(c => c.SubjectId == StoreConversationTenantAgentByIdCommandHandlerTesting.SubjectId),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static StoreConversationTenantAgentByIdCommand BuildCommand(int agentId, string sessionId, string role, string text)
        {
            return new StoreConversationTenantAgentByIdCommand
            {
                Id = agentId,
                SessionId = sessionId,
                Messages = new List<StoreConversationMessageInput>
                {
                    new StoreConversationMessageInput { Role = role, Text = text }
                }
            };
        }
    }
}
