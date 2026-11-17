using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgentConversations
{
    public class ListTenantAgentConversationsQuery : IWkfloRequest<Result>
    {
        public int Id { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;

        /// <summary>
        /// When true (the default), test conversations (<c>Conversation.IsTestChat</c>) are excluded from the
        /// list. CMS sends this as a query param driven by the Chat Logs "Hide test chats" toggle.
        /// </summary>
        public bool HideTestChats { get; set; } = true;
    }
}

