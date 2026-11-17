namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows
{
    public class ConversationAnalysisWorkflowInput
    {
        public string TenantName { get; set; }
        public int AgentId { get; set; }
        public string SessionId { get; set; }
        public string UserId { get; set; }
    }
}
