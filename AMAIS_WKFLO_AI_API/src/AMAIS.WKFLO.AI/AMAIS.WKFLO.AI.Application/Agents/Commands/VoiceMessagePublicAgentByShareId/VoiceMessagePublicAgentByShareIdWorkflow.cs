using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId
{
    public class VoiceMessagePublicAgentByShareIdWorkflow : Workflow<string, bool>
    {
        public async override Task<bool> RunAsync(WorkflowContext context, string message)
        {
            await context.CallActivityAsync(nameof(VoiceMessagePublicAgentByShareIdWorkflowActivity), message);
            return true;
        }
    }
}