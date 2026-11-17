using Dapr.Workflow;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows
{
    public class DaprConversationTitleWorkflowScheduler : IConversationTitleWorkflowScheduler
    {
        private readonly DaprWorkflowClient _workflowClient;

        public DaprConversationTitleWorkflowScheduler(DaprWorkflowClient workflowClient)
        {
            _workflowClient = workflowClient;
        }

        public Task ScheduleAsync(string instanceId, ConversationTitleWorkflowInput input) =>
            _workflowClient.ScheduleNewWorkflowAsync("ConversationTitleWorkflow", instanceId, input);
    }
}
