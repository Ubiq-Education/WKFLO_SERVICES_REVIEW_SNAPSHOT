using Dapr.Workflow;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval
{
    public class ToolApprovalExpiryWorkflowInput
    {
        public string ApprovalId { get; set; }
    }

    /// <summary>
    /// WI 7575: schedules the durable 10-minute expiry of an approval request (a Dapr workflow timer, so it
    /// survives restarts and runs on whichever instance picks it up). Thin seam over the sealed
    /// <see cref="DaprWorkflowClient"/> for testability, like <c>IMessageResolutionWorkflowScheduler</c>.
    /// </summary>
    public interface IToolApprovalExpiryScheduler
    {
        Task ScheduleAsync(ToolApprovalExpiryWorkflowInput input);
    }

    public class DaprToolApprovalExpiryScheduler : IToolApprovalExpiryScheduler
    {
        public const string WorkflowName = "ToolApprovalExpiryWorkflow";

        private readonly DaprWorkflowClient _workflowClient;

        public DaprToolApprovalExpiryScheduler(DaprWorkflowClient workflowClient)
        {
            _workflowClient = workflowClient;
        }

        public Task ScheduleAsync(ToolApprovalExpiryWorkflowInput input) =>
            _workflowClient.ScheduleNewWorkflowAsync(WorkflowName, $"tool-approval-expiry-{input.ApprovalId}", input);
    }
}
