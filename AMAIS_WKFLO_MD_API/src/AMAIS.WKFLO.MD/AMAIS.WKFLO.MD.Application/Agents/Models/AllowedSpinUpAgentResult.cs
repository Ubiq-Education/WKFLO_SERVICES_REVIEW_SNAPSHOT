using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class AllowedSpinUpAgentResult : IMap
    {
        public int AgentId { get; set; }
        public int AllowedSpinUpAgentId { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<AgentAllowedSpinUpAgent, AllowedSpinUpAgentResult>();
        }
    }
}
