using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows
{
    public class ConversationAnalysisWorkflow : Workflow<ConversationAnalysisWorkflowInput, bool>
    {
        public override async Task<bool> RunAsync(WorkflowContext context, ConversationAnalysisWorkflowInput input)
        {
#if DEBUG
            await context.CreateTimer(TimeSpan.FromSeconds(30));
#else
            await context.CreateTimer(TimeSpan.FromMinutes(30));
#endif
            await context.CallActivityAsync(nameof(ConversationAnalysisWorkflowActivity), input);
            return true;
        }
    }
}
