using Dapr.Actors;

namespace AMAIS.WKFLO.AI.Application.Agents.Actors
{
    /// <summary>
    /// Gatekeeper for cross-agent async notifications (story #6480 / task #6589), addressed by an
    /// actor ID of "{SessionId}:{AgentId}" so all events for a given session/agent pair are
    /// serialized through the same actor instance. <see cref="EnqueueEventAsync"/> only takes the
    /// existing <c>statestore-ai</c> correlation key (never the queued payload itself) so this
    /// actor's remoting proxy never has to serialize Semantic Kernel types — the reason the
    /// original AgentActor (task #6482) was removed in favor of the current <c>IAgentInvoker</c>.
    /// </summary>
    public interface IAgentActor : IActor
    {
        Task EnqueueEventAsync(string messageKey);

        /// <summary>
        /// US 7501 (approved change 3): the person stopped the session. Drops the hand-offs queued before
        /// <paramref name="stoppedAt"/> and deletes their <c>msg:</c> state; ones queued after it are kept.
        /// </summary>
        /// <returns>How many hand-offs were dropped (US 7502: the Stop then saves a marker for the chat).</returns>
        Task<int> ClearQueueAsync(DateTimeOffset stoppedAt);
    }
}
