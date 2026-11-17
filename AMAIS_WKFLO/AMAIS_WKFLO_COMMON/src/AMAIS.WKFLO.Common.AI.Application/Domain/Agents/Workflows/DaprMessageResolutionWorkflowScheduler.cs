using Dapr.Workflow;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows
{
    public class DaprMessageResolutionWorkflowScheduler : IMessageResolutionWorkflowScheduler
    {
        private readonly DaprWorkflowClient _workflowClient;

        public DaprMessageResolutionWorkflowScheduler(DaprWorkflowClient workflowClient)
        {
            _workflowClient = workflowClient;
        }

        public Task ScheduleAsync(string instanceId, MessageResolutionWorkflowInput input) =>
            _workflowClient.ScheduleNewWorkflowAsync("MessageResolutionWorkflow", instanceId, input);
    }
}
