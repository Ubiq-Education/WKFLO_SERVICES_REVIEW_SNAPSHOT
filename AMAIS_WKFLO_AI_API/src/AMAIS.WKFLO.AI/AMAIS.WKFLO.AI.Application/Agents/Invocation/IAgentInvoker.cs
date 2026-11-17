using AMAIS.WKFLO.AI.Application.Agents.Models;

namespace AMAIS.WKFLO.AI.Application.Agents.Invocation
{
    /// <summary>
    /// Runs a single tenant/published agent as one role/step of a multi-agent orchestration, or in
    /// response to a cross-agent pub/sub event. Replaces the former <c>IAgentActor</c>/<c>AgentActor</c>
    /// Dapr Actor (see #6553): each call was already a fresh, one-shot request/response with no
    /// reminders/timers and no state surviving past the call, so it never needed the Dapr Actor
    /// runtime — a plain scoped service, invoked directly from a Workflow Activity or a pub/sub
    /// controller (both of which already provide a fresh DI scope per invocation), is enough.
    /// </summary>
    public interface IAgentInvoker
    {
        Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default);
    }
}
