namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows
{
    /// <summary>
    /// US 7507: input of <c>ConversationTitleWorkflow</c>. Carries the first exchange itself, so the title can be
    /// generated whether or not the agent persists its chat history (EnableSTM).
    /// </summary>
    public class ConversationTitleWorkflowInput
    {
        public string TenantName { get; set; }
        public int AgentId { get; set; }
        public string SessionId { get; set; }
        public string FirstUserMessage { get; set; }
        public string FirstAssistantMessage { get; set; }
    }
}
