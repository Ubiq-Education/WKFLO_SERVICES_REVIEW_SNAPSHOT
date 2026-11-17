using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentChatDeleteResult : IMapFrom<Agent>
    {
        public string SessionId { get; set; }
    }
}
