using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.AI;
using Mapster;

namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    public class PublicAgentDataResult : IMap
    {
        public string Message { get; set; }

        /// <summary>
        /// US 7501: true when the user stopped this synchronous run. There is then no <see cref="Message"/>; the chat
        /// already shows the Stop (and its "Stopped" divider).
        /// </summary>
        public bool Stopped { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<PublicAgentData, PublicAgentDataResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<PublicAgentData, PublicAgentDataResult>();
        }
    }
}

