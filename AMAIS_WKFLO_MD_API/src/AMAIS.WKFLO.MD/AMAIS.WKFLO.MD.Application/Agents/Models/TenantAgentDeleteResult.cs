using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentDeleteResult : IMapFrom<Agent>
    {
        public int Id { get; set; }
    }
}
