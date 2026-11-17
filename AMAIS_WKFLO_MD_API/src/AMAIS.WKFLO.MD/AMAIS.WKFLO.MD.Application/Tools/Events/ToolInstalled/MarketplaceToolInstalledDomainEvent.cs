using AMAIS.WKFLO.Common.Mediator;

namespace AMAIS.WKFLO.MD.Application.Tools.Events.ToolInstalled
{
    public class MarketplaceToolInstalledDomainEvent : IWkfloNotification
    {
        public int ToolId { get; set; }
        public int TotalInstallations { get; set; }
        public int Installations { get; set; }

        public MarketplaceToolInstalledDomainEvent(
            int agentId,
            int totalInstallations,
            int installations)
        {
            ToolId = agentId;
            TotalInstallations = totalInstallations;
            Installations = installations;
        }
    }
}
