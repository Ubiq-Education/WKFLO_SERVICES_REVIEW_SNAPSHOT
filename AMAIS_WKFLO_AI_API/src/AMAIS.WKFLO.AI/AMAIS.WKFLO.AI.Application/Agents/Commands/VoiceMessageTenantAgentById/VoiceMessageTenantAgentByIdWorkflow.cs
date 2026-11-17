using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessageTenantAgentById
{
    public class VoiceMessageTenantAgentByIdWorkflow : Workflow<string, bool>
    {
        public async override Task<bool> RunAsync(WorkflowContext context, string message)
        {
            await context.CallActivityAsync(nameof(VoiceMessageTenantAgentByIdWorkflowActivity), message);
            return true;
        }
    }
}