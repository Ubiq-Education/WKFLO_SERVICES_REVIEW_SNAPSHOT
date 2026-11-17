using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows
{
    /// <summary>
    /// US 7507: generates a MAISY conversation's AI title right after its first completed exchange.
    /// No retry: the title is attempted once (see ConversationTitleWorkflowActivity).
    /// </summary>
    public class ConversationTitleWorkflow : Workflow<ConversationTitleWorkflowInput, bool>
    {
        public override async Task<bool> RunAsync(WorkflowContext context, ConversationTitleWorkflowInput input)
        {
            return await context.CallActivityAsync<bool>(nameof(ConversationTitleWorkflowActivity), input);
        }
    }
}
