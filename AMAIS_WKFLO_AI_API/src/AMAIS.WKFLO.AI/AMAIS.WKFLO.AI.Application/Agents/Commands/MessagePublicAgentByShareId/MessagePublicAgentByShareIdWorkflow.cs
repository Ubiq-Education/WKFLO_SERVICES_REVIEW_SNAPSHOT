using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId
{
    public class MessagePublicAgentByShareIdWorkflow : Workflow<string, bool>
    {
        public async override Task<bool> RunAsync(WorkflowContext context, string message)
        {
            await context.CallActivityAsync(nameof(MessagePublicAgentByShareIdWorkflowActivity), message);
            return true;
        }
    }

}