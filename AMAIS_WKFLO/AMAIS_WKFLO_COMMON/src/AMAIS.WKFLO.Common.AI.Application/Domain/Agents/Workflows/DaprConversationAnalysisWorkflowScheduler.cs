using Dapr.Workflow;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows
{
    public class DaprConversationAnalysisWorkflowScheduler : IConversationAnalysisWorkflowScheduler
    {
        private readonly DaprWorkflowClient _workflowClient;

        public DaprConversationAnalysisWorkflowScheduler(DaprWorkflowClient workflowClient)
        {
            _workflowClient = workflowClient;
        }

        public Task ScheduleAsync(string instanceId, ConversationAnalysisWorkflowInput input) =>
            _workflowClient.ScheduleNewWorkflowAsync("ConversationAnalysisWorkflow", instanceId, input);
    }
}
