using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class MarketplaceAgentDeleteResult : IMapFrom<Agent>
    {
        public int Id { get; set; }
    }
}
