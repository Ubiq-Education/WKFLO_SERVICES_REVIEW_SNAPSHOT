using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentConversationBySessionId;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetTenantAgentConversationBySessionId
{
    public class GetTenantAgentConversationBySessionIdQueryHandlerTests : IClassFixture<GetTenantAgentConversationBySessionIdQueryHandlerTesting>
    {
        private readonly GetTenantAgentConversationBySessionIdQueryHandlerTesting _testing;

        public GetTenantAgentConversationBySessionIdQueryHandlerTests(GetTenantAgentConversationBySessionIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, "session-abc", ResultType.Ok)]
        [InlineData(1, "session-xyz", ResultType.Ok)]
        [InlineData(1, "session-notfound", ResultType.NotFound)]
        [InlineData(99, "session-abc", ResultType.NotFound)]
        public async Task TestHandler(int agentId, string sessionId, ResultType resultType)
        {
            // Act
            var result = await _testing.GetTenantAgentConversationBySessionIdQueryHandler.Handle(new GetTenantAgentConversationBySessionIdQuery
            {
                Id = agentId,
                SessionId = sessionId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<TenantAgentConversationResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(agentId, success.Data.AgentId);
                Assert.Equal(sessionId, success.Data.SessionId);
                Assert.NotEmpty(success.Data.Messages);
            }
        }

        [Fact]
        public async Task Handler_ReturnsResolutionStatusAndLastActivity()
        {
            var result = await _testing.GetTenantAgentConversationBySessionIdQueryHandler.Handle(new GetTenantAgentConversationBySessionIdQuery
            {
                Id = 1,
                SessionId = "session-xyz"
            }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantAgentConversationResult>>(result);
            Assert.False(success.Data.IsResolved);
            Assert.Equal(GetTenantAgentConversationBySessionIdQueryHandlerTesting.ResolvedActivityAt, success.Data.LastActivityAt);
        }

        [Fact]
        public async Task Handler_ReturnsNullLastActivity_WhenNeverAnalyzed()
        {
            var result = await _testing.GetTenantAgentConversationBySessionIdQueryHandler.Handle(new GetTenantAgentConversationBySessionIdQuery
            {
                Id = 1,
                SessionId = "session-abc"
            }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantAgentConversationResult>>(result);
            Assert.True(success.Data.IsResolved);
            Assert.Null(success.Data.LastActivityAt);
        }
    }
}
