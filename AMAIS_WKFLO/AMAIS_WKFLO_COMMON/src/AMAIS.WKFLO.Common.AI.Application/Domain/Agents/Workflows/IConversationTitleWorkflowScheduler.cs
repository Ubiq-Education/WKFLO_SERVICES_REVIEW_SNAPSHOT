namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows
{
    /// <summary>
    /// US 7507: thin, mockable seam over <c>Dapr.Workflow.DaprWorkflowClient</c> (sealed) for scheduling
    /// <c>ConversationTitleWorkflow</c> instances. Same reason to exist as <see cref="IConversationAnalysisWorkflowScheduler"/>.
    /// </summary>
    public interface IConversationTitleWorkflowScheduler
    {
        Task ScheduleAsync(string instanceId, ConversationTitleWorkflowInput input);
    }
}
