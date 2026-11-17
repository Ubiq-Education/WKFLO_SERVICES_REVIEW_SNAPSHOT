using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UnassignAllowedSpinUpAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UnassignAllowedSpinUpAgent
{
    public class UnassignAllowedSpinUpAgentCommandHandlerTests(UnassignAllowedSpinUpAgentCommandHandlerTesting testing) : IClassFixture<UnassignAllowedSpinUpAgentCommandHandlerTesting>
    {
        private UnassignAllowedSpinUpAgentCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData(1, 2, ResultType.Ok)]
        [InlineData(1, 99, ResultType.NotFound)]
        [InlineData(99, 2, ResultType.NotFound)]
        public async Task TestHandler(int agentId, int allowedSpinUpAgentId, ResultType resultType)
        {
            var result = await Testing.UnassignAllowedSpinUpAgentCommandHandler.Handle(new UnassignAllowedSpinUpAgentCommand
            {
                Id = agentId,
                AllowedSpinUpAgentId = allowedSpinUpAgentId
            }, TestContext.Current.CancellationToken);

            Assert.Equal(resultType, result.ResultType);
            if (result is SuccessResult<AllowedSpinUpAgentResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(agentId, success.Data.AgentId);
                Assert.Equal(allowedSpinUpAgentId, success.Data.AllowedSpinUpAgentId);
            }
        }
    }
}
