using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById
{
    public class MessageTenantAgentByIdWorkflow : Workflow<string, bool>
    {
        public async override Task<bool> RunAsync(WorkflowContext context, string message)
        {
            await context.CallActivityAsync(nameof(MessageTenantAgentByIdWorkflowActivity), message);
            return true;
        }
    }
}