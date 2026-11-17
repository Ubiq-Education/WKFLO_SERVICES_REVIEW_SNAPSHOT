using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgentChat;
using AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Moq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.CreateTenantAgentChat
{
    public class CreateTenantAgentChatCommandHandlerTests : IClassFixture<CreateTenantAgentChatCommandHandlerTesting>
    {
        private readonly CreateTenantAgentChatCommandHandlerTesting _testing;

        public CreateTenantAgentChatCommandHandlerTests(CreateTenantAgentChatCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, "session-abc", ResultType.Ok)]
        [InlineData(2, "session-xyz", ResultType.Ok)]
        public async Task TestHandler(int agentId, string sessionId, ResultType resultType)
        {
            // Act
            var result = await _testing.CreateTenantAgentChatCommandHandler.Handle(new CreateTenantAgentChatCommand
            {
                Id = agentId,
                SessionId = sessionId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<TenantAgentChatCreateResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(sessionId, success.Data.SessionId);
            }
            else
            {
                Assert.Fail($"Expected SuccessResult but got {result.ResultType}");
            }
        }

        [Fact]
        public async Task TestHandler_SyncsConversationWithSubjectIdFromChatContext()
        {
            // Act
            await _testing.CreateTenantAgentChatCommandHandler.Handle(new CreateTenantAgentChatCommand
            {
                Id = 1,
                SessionId = "session-subject-id"
            }, TestContext.Current.CancellationToken);

            // Assert
            // Filters on this test's own unique SessionId — the mediator mock is shared across the whole test
            // class via IClassFixture, so other test methods' Send calls are also recorded on it.
            _testing.MediatorMock.Verify(x => x.Send(
                It.Is<SyncConversationCommand>(c =>
                    c.SessionId == "session-subject-id"
                    && c.SubjectId == CreateTenantAgentChatCommandHandlerTesting.SubjectId),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
