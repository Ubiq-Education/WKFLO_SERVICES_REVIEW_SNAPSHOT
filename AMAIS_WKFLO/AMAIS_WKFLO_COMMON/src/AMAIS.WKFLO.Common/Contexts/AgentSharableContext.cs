using AMAIS.WKFLO.Common.Mappings;
using Mapster;

namespace AMAIS.WKFLO.Common.Contexts
{
    public class AgentSharableContext : IMap
    {
        public string ShareId { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<AgentSharableContext, AgentSharableContext>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<AgentSharableContext, AgentSharableContext>();
        }
    }
}

