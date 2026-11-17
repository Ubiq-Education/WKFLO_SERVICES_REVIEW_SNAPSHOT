using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentConversationResult : IMapFrom<Conversation>
    {
        public int Id { get; set; }
        public int AgentId { get; set; }
        public string SessionId { get; set; }
        public string Name { get; set; }
        public DateTimeOffset? ConversationDate { get; set; }
        public int TotalThumbsUp { get; set; }
        public int TotalThumbsDown { get; set; }
        public bool IsTestChat { get; set; }
        public bool IsResolved { get; set; }
        public DateTimeOffset? LastActivityAt { get; set; }
        public List<TenantAgentChatMessageResult> Messages { get; set; } = new();

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<Conversation, TenantAgentConversationResult>();
        //         }
    }
}
