using AMAIS.WKFLO.Common.Models;
using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.Application.Agents.Actors
{
    /// <summary>
    /// Pure queue logic for <see cref="AgentActor"/>, extracted out of the actor so it can be unit
    /// tested directly: Dapr's actor test harness (<c>ActorHost.CreateForTest</c>) requires an actor
    /// state provider that can only be set from inside the <c>Dapr.Actors</c> assembly itself, so
    /// <see cref="AgentActor"/>'s own state reads/writes, reminders, and workflow scheduling/waiting
    /// are not unit-testable from here — this class holds everything that is.
    ///
    /// There is deliberately no "is a workflow running" state here (or anywhere in the actor). An
    /// earlier revision tracked that as a persisted bool + instance ID, reconciled via reminder-driven
    /// polling of <c>GetWorkflowStateAsync</c> — but a <c>null</c> query result (which can legitimately
    /// mean "not persisted yet," not "gone") got misread as "finished," letting the actor advance past
    /// a workflow that was still active; a later re-enqueue of the same event then tried to reschedule
    /// that still-active instance ID, which Dapr's workflow engine rejected. <see cref="AgentActor"/>
    /// now blocks its own turn on <c>WaitForWorkflowCompletionAsync</c> instead — Dapr's turn-based
    /// per-actor-ID concurrency then makes "only one workflow at a time" a structural guarantee rather
    /// than a fact tracked in state that could drift out of sync with reality.
    /// </summary>
    public static class AgentActorGatekeeper
    {
        /// <summary>
        /// Adds <paramref name="messageKey"/> to <paramref name="queue"/> unless already present.
        /// Returns whether it was added (i.e. it wasn't a duplicate).
        /// </summary>
        public static bool TryEnqueue(List<string> queue, string messageKey)
        {
            if (queue.Contains(messageKey))
                return false;

            queue.Add(messageKey);
            return true;
        }

        /// <summary>
        /// Walks the front of <paramref name="queue"/>, dropping any malformed keys (missing the
        /// expected prefix), until it finds the next valid key to process — or the queue is exhausted.
        /// The returned candidate is left at the front of <paramref name="queue"/> (not removed): it's
        /// the caller's job to remove it once processing has actually completed.
        /// </summary>
        public static bool TryDequeueNextCandidate(List<string> queue, out string key, out string instanceId)
        {
            while (queue.Count > 0)
            {
                var candidate = queue[0];
                if (TryGetInstanceId(candidate, out instanceId))
                {
                    key = candidate;
                    return true;
                }

                // Malformed key (missing the expected prefix) — drop it and try the next one.
                queue.RemoveAt(0);
            }

            key = null;
            instanceId = null;
            return false;
        }

        /// <summary>
        /// Strips the <c>statestore-ai</c> correlation key prefix to get the Dapr workflow instance ID,
        /// the same derivation <c>AgentEventSubscriber</c> used before this actor existed. Rejects a key
        /// that is exactly the prefix with nothing after it, which would otherwise yield an empty instance ID.
        /// </summary>
        public static bool TryGetInstanceId(string messageKey, out string instanceId)
        {
            var prefix = CommonConsts.CommonAiPubSubConsts.MessageKeyPrefix;
            if (!string.IsNullOrEmpty(messageKey) && messageKey.Length > prefix.Length && messageKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                instanceId = messageKey[prefix.Length..];
                return true;
            }

            instanceId = null;
            return false;
        }

        /// <summary>
        /// Whether a queried workflow status counts as "already done" — used by <see cref="AgentActor"/>
        /// to decide, for the head-of-queue candidate, whether it still needs to schedule and/or wait for
        /// it (see the class remarks for why a <c>null</c> status is deliberately NOT treated as finished).
        /// </summary>
        public static bool IsWorkflowFinished(WorkflowRuntimeStatus? status) =>
            status is WorkflowRuntimeStatus.Completed or WorkflowRuntimeStatus.Failed or WorkflowRuntimeStatus.Terminated;

        /// <summary>
        /// The actor ID is <c>"{SessionId}:{AgentId}"</c> (see <see cref="IAgentActor"/>). Builds it the same way
        /// <c>AgentEventSubscriber</c> does.
        /// </summary>
        public static string BuildActorId(string sessionId, int agentId) => $"{sessionId}:{agentId}";

        /// <summary>US 7502: the agent id of an actor id built by <see cref="BuildActorId"/>.</summary>
        public static bool TryGetAgentId(string actorId, out int agentId)
        {
            agentId = 0;
            var separator = actorId?.LastIndexOf(':') ?? -1;
            return separator >= 0 && int.TryParse(actorId[(separator + 1)..], out agentId);
        }

        /// <summary>
        /// US 7501: the session part of an <see cref="BuildActorId"/> actor ID. Split on the last ':' because only
        /// the agent part is known not to contain one.
        /// </summary>
        public static bool TryGetSessionId(string actorId, out string sessionId)
        {
            var separator = actorId?.LastIndexOf(':') ?? -1;
            if (separator > 0 && int.TryParse(actorId.AsSpan(separator + 1), out _))
            {
                sessionId = actorId[..separator];
                return true;
            }

            sessionId = null;
            return false;
        }

        /// <summary>
        /// US 7501: whether a queued hand-off belongs to a run the person has stopped - it was queued before the
        /// session's last Stop. Hand-offs queued after it (the person's next message) are not affected, and one with
        /// no known queue time (queued before this was recorded) is kept rather than guessed at.
        /// </summary>
        public static bool IsQueuedBeforeStop(DateTimeOffset? queuedAt, DateTimeOffset? stoppedAt) =>
            queuedAt is { } queued && stoppedAt is { } stopped && queued < stopped;

        /// <summary>
        /// US 7501 (approved change 3): removes from <paramref name="queue"/> every key queued before
        /// <paramref name="stoppedAt"/> and returns them, in queue order, so the caller can delete their payloads.
        /// </summary>
        public static List<string> DropQueuedBeforeStop(List<string> queue, IReadOnlyDictionary<string, DateTimeOffset?> queuedAtByKey, DateTimeOffset stoppedAt)
        {
            var dropped = queue
                .Where(key => queuedAtByKey.TryGetValue(key, out var queuedAt) && IsQueuedBeforeStop(queuedAt, stoppedAt))
                .ToList();

            queue.RemoveAll(dropped.Contains);
            return dropped;
        }
    }
}
