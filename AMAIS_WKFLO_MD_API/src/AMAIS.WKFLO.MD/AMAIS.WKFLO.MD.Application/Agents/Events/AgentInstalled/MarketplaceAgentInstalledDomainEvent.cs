using AMAIS.WKFLO.Common.Mediator;

namespace AMAIS.WKFLO.MD.Application.Agents.Events.AgentInstalled
{
    public class MarketplaceAgentInstalledDomainEvent : IWkfloNotification
    {
        public int AgentId { get; set; }
        public int TotalInstallations { get; set; }
        public int Installations { get; set; }

        public MarketplaceAgentInstalledDomainEvent(
            int agentId,
            int totalInstallations,
            int installations)
        {
            AgentId = agentId;
            TotalInstallations = totalInstallations;
            Installations = installations;
        }
    }
}
