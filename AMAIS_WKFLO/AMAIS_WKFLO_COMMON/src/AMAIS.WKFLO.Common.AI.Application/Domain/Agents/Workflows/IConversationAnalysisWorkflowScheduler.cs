namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows
{
    /// <summary>
    /// Thin seam over <c>Dapr.Workflow.DaprWorkflowClient</c> for scheduling <c>ConversationAnalysisWorkflow</c>
    /// instances. Exists purely for testability: <c>DaprWorkflowClient</c> is sealed, so it cannot be mocked
    /// directly by callers (like <c>UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler</c>) that need to
    /// verify scheduling actually happens.
    /// </summary>
    public interface IConversationAnalysisWorkflowScheduler
    {
        Task ScheduleAsync(string instanceId, ConversationAnalysisWorkflowInput input);
    }
}
