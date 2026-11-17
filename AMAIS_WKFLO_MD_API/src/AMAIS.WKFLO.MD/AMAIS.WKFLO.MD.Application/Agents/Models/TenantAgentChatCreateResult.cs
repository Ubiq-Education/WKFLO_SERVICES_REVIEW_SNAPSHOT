using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgentChat;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentChatCreateResult : IMap
    {
        public string Id { get; set; }
        public string SessionId { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<CreateTenantAgentChatCommand, TenantAgentChatCreateResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<CreateTenantAgentChatCommand, TenantAgentChatCreateResult>();
        }
    }
}
