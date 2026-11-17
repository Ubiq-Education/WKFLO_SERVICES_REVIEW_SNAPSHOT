using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UnassignAllowedSpinUpAgent;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UnassignAllowedSpinUpAgent
{
    public class UnassignAllowedSpinUpAgentCommandHandlerTesting
    {
        public UnassignAllowedSpinUpAgentCommandHandler UnassignAllowedSpinUpAgentCommandHandler { get; set; }

        public UnassignAllowedSpinUpAgentCommandHandlerTesting()
        {
            var existingAllowedSpinUpAgent = new AgentAllowedSpinUpAgent()
            {
                AgentId = 1,
                AllowedSpinUpAgentId = 2
            };

            var agentAllowedSpinUpAgents = new List<AgentAllowedSpinUpAgent>() { existingAllowedSpinUpAgent }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentAllowedSpinUpAgents).Returns(agentAllowedSpinUpAgents.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UnassignAllowedSpinUpAgentCommandHandler = new UnassignAllowedSpinUpAgentCommandHandler(db.Object);
        }
    }
}
