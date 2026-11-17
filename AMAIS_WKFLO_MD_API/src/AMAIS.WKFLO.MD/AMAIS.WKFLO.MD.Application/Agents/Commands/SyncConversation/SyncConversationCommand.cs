using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation
{
    public class SyncConversationCommand : IWkfloRequest<Result>
    {
        public int AgentId { get; set; }
        public string TenantName { get; set; }
        public string SessionId { get; set; }
        public string Name { get; set; }
        public DateTimeOffset? ConversationDate { get; set; }
        public string UserId { get; set; }
        public string SubjectId { get; set; }
        public int TotalThumbsUp { get; set; }
        public int TotalThumbsDown { get; set; }

        /// <summary>
        /// True when this sync originated from a "test chat" surface. Once persisted as true on
        /// <c>Conversation.IsTestChat</c>, it must never revert to false — see
        /// <see cref="SyncConversationCommandHandler"/>.
        /// </summary>
        public bool IsTestChat { get; set; }
    }
}

