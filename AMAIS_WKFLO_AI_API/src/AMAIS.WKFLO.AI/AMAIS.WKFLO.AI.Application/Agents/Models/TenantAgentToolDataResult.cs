using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.AI;
using Mapster;

namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    public class TenantAgentToolDataResult : IMap
    {
        public string Name { get; set; }
        public object Data { get; set; }
        public string Status { get; set; }
        public int StatusCode { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<TenantAgentToolData, TenantAgentToolDataResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<TenantAgentToolData, TenantAgentToolDataResult>();
        }
    }
}

