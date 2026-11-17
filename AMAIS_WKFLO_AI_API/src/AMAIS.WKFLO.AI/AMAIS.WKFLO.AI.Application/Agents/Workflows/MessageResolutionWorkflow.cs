using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows
{
    /// <summary>
    /// Near-immediate (no debounce) message-level resolution judgment — the counterpart to
    /// <see cref="ConversationAnalysisWorkflow"/>'s whole-conversation judgment, but scoped to a single
    /// question/answer exchange and run essentially right away, so the "Recent resolved/unresolved
    /// questions" panels don't have to wait 30 minutes (or the conversation ending) to get a real signal
    /// for a message that already has its answer.
    /// </summary>
    public class MessageResolutionWorkflow : Workflow<MessageResolutionWorkflowInput, bool>
    {
        public override async Task<bool> RunAsync(WorkflowContext context, MessageResolutionWorkflowInput input)
        {
            await context.CallActivityAsync(nameof(MessageResolutionWorkflowActivity), input);
            return true;
        }
    }
}
