using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results.Chat;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentChatListResult : IMap
    {
        public int AgentId { get; set; }
        public string SessionId { get; set; }
        public string Name { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string UserId { get; set; }
        public int TotalThumbsUp { get; set; }
        public int TotalThumbsDown { get; set; }
        public bool IsResolved { get; set; }
        public DateTimeOffset? LastActivityAt { get; set; }
        public bool IsTestChat { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<ChatListResult, TenantAgentChatListResult>();
        //             profile.CreateMap<Conversation, TenantAgentChatListResult>()
        //                 .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.ConversationDate ?? (DateTimeOffset)src.CreatedOn));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<ChatListResult, TenantAgentChatListResult>();
            config.NewConfig<Conversation, TenantAgentChatListResult>()
                .Map(dest => dest.CreatedAt, src => src.ConversationDate ?? (DateTimeOffset)src.CreatedOn);
        }
    }
}
