using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.AssignAllowedSpinUpAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.AssignAllowedSpinUpAgent
{
    public class AssignAllowedSpinUpAgentCommandHandlerTests(AssignAllowedSpinUpAgentCommandHandlerTesting testing) : IClassFixture<AssignAllowedSpinUpAgentCommandHandlerTesting>
    {
        private AssignAllowedSpinUpAgentCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData(1, 3, ResultType.Ok)]
        [InlineData(1, 2, ResultType.Invalid)]
        public async Task TestHandler(int agentId, int allowedSpinUpAgentId, ResultType resultType)
        {
            var result = await Testing.AssignAllowedSpinUpAgentCommandHandler.Handle(new AssignAllowedSpinUpAgentCommand
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
