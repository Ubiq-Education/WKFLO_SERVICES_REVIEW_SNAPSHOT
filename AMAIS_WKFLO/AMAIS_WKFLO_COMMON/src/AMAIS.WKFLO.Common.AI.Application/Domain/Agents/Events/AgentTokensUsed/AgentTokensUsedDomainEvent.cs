using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed
{
    public class AgentTokensUsedDomainEvent : IWkfloNotification
    {
        public AgentAnswer AgentAnswer { get; }

        public AgentTokensUsedDomainEvent(AgentAnswer agentAnswer)
        {
            AgentAnswer = agentAnswer;
        }
    }
}
