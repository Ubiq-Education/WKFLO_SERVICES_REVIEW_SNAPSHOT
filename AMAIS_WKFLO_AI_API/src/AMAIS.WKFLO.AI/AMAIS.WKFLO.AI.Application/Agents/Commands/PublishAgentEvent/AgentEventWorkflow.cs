using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent
{
    /// <summary>
    /// Trivial single-activity wrapper, scheduled by <c>AgentEventSubscriber</c> — aligns cross-agent
    /// event handling with every other pub/sub subscriber in this codebase (see
    /// <c>AIWorkerSubscriber</c>/<c>MessageTenantAgentByIdWorkflow</c>), instead of doing the real work
    /// inline in the controller. <paramref name="message"/> is the state-store correlation key, not
    /// the resolved payload — <see cref="AgentEventWorkflowActivity"/> loads the full
    /// <c>WkfloQueueMessage&lt;AgentEventMessage&gt;</c> itself.
    /// </summary>
    public class AgentEventWorkflow : Workflow<string, bool>
    {
        public async override Task<bool> RunAsync(WorkflowContext context, string message)
        {
            return await context.CallActivityAsync<bool>(nameof(AgentEventWorkflowActivity), message);
        }
    }
}
