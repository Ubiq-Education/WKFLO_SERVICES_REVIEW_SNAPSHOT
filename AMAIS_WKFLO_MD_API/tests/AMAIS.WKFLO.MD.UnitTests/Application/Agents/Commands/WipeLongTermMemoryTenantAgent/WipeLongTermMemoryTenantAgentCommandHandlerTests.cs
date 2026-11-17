using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.WipeLongTermMemoryTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.WipeLongTermMemoryTenantAgent
{
    public class WipeLongTermMemoryTenantAgentCommandHandlerTests : IClassFixture<WipeLongTermMemoryTenantAgentCommandHandlerTesting>
    {
        private readonly WipeLongTermMemoryTenantAgentCommandHandlerTesting _testing;

        public WipeLongTermMemoryTenantAgentCommandHandlerTests(WipeLongTermMemoryTenantAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(999, 0)]
        public async Task Handle_ReturnsOk_WithExpectedAffectedUsersCount(int agentId, int expectedAffectedUsers)
        {
            // Act
            var result = await _testing.Handler.Handle(new WipeLongTermMemoryTenantAgentCommand { Id = agentId }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<TenantAgentWipeLongTermMemoryResult> success)
            {
                Assert.Equal(agentId, success.Data.AgentId);
                Assert.Equal(expectedAffectedUsers, success.Data.AffectedUsers);
            }
            else
            {
                Assert.Fail("Expected SuccessResult<TenantAgentWipeLongTermMemoryResult>");
            }
        }
    }
}
