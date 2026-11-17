namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentConversationsPageResult
    {
        public List<TenantAgentChatListResult> Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
}
