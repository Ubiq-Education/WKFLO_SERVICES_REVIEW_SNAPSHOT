using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Models;
using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Actors
{
    /// <summary>
    /// US 7501: the state reads <see cref="AgentActor"/> needs to keep a stopped session's hand-offs from running.
    /// Every read is a single read at a point the actor already passes (dequeue, or a Stop) - never a loop or a timer.
    /// Kept out of the actor so it can be unit tested (see <see cref="AgentActorGatekeeper"/>'s remarks).
    /// </summary>
    public interface IHandOffQueueStopGuard
    {
        /// <summary>
        /// Dequeue checkpoint: whether the hand-off <paramref name="messageKey"/> of actor <paramref name="actorId"/>
        /// was queued before the last Stop of its own (tenant, session), so it must not start. Two single reads: the
        /// hand-off's queue time and tenant, then that tenant's stop state for the session.
        /// </summary>
        Task<bool> IsStoppedAsync(string actorId, string messageKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// US 7502: same check as <see cref="IsStoppedAsync"/>, returning the Stop that drops the hand-off (its tenant and
        /// stoppedAt), or null when it must run.
        /// </summary>
        Task<HandOffQueueStopGuard.DroppingStop> GetDroppingStopAsync(string actorId, string messageKey, CancellationToken cancellationToken = default);

        /// <summary>The queue time of each hand-off in <paramref name="messageKeys"/> (null when unknown).</summary>
        Task<IReadOnlyDictionary<string, DateTimeOffset?>> GetQueuedAtAsync(IEnumerable<string> messageKeys, CancellationToken cancellationToken = default);

        /// <summary>Deletes the queued hand-off's <c>msg:</c> payload. Best effort.</summary>
        Task DeleteQueuedMessageAsync(string messageKey, CancellationToken cancellationToken = default);
    }

    public sealed class HandOffQueueStopGuard : IHandOffQueueStopGuard
    {
        private readonly IChatStopStateStore _stopStateStore;
        private readonly DaprClient _daprClient;
        private readonly ILogger<HandOffQueueStopGuard> _logger;

        public HandOffQueueStopGuard(IChatStopStateStore stopStateStore, DaprClient daprClient, ILogger<HandOffQueueStopGuard> logger)
        {
            _stopStateStore = stopStateStore;
            _daprClient = daprClient;
            _logger = logger;
        }

        public async Task<bool> IsStoppedAsync(string actorId, string messageKey, CancellationToken cancellationToken = default) =>
            await GetDroppingStopAsync(actorId, messageKey, cancellationToken) != null;

        public async Task<DroppingStop> GetDroppingStopAsync(string actorId, string messageKey, CancellationToken cancellationToken = default)
        {
            if (!AgentActorGatekeeper.TryGetSessionId(actorId, out var sessionId))
                return null;

            // The Stop is scoped by tenant: the hand-off's own tenant (from its stored message) decides which stop state applies.
            var stamp = await ReadStampAsync(messageKey, cancellationToken);
            var tenantName = stamp?.TenantContext?.TenantName;
            if (string.IsNullOrWhiteSpace(tenantName))
                return null;

            DateTimeOffset? stoppedAt;
            try
            {
                stoppedAt = await _stopStateStore.GetStoppedAtAsync(tenantName, sessionId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only a safety net: the hand-off's own run also reads the stop state when it starts.
                _logger.LogWarning(ex, "AgentActor {ActorId}: reading the stop state failed, not skipping {Key}", actorId, messageKey);
                return null;
            }

            if (stoppedAt == null)
                return null;

            if (!AgentActorGatekeeper.IsQueuedBeforeStop(stamp.QueuedAt, stoppedAt))
                return null;

            _logger.LogInformation("AgentActor {ActorId}: hand-off {Key} of session {SessionId} (tenant {TenantName}) dropped at dequeue: queued at {QueuedAt}, stopped at {StoppedAt}",
                actorId, messageKey, sessionId, tenantName, stamp.QueuedAt, stoppedAt);

            return new DroppingStop(tenantName, sessionId, stoppedAt.Value);
        }

        public async Task<IReadOnlyDictionary<string, DateTimeOffset?>> GetQueuedAtAsync(IEnumerable<string> messageKeys, CancellationToken cancellationToken = default)
        {
            var queuedAtByKey = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
            foreach (var key in messageKeys.Distinct(StringComparer.Ordinal))
            {
                queuedAtByKey[key] = await ReadQueuedAtAsync(key, cancellationToken);
            }

            return queuedAtByKey;
        }

        public async Task DeleteQueuedMessageAsync(string messageKey, CancellationToken cancellationToken = default)
        {
            try
            {
                await _daprClient.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, messageKey, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Deleting the dropped hand-off state {Key} failed", messageKey);
            }
        }

        private async Task<DateTimeOffset?> ReadQueuedAtAsync(string messageKey, CancellationToken cancellationToken) =>
            (await ReadStampAsync(messageKey, cancellationToken))?.QueuedAt;

        private async Task<QueuedMessageStamp> ReadStampAsync(string messageKey, CancellationToken cancellationToken)
        {
            try
            {
                // Only the queue time and tenant are deserialized, not the hand-off payload itself.
                return await _daprClient.GetStateAsync<QueuedMessageStamp>(CommonConsts.CommonAiPubSubConsts.StateStoreName, messageKey,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Reading the queue time of hand-off {Key} failed", messageKey);
                return null;
            }
        }

        /// <summary>US 7502: the Stop that drops a queued hand-off.</summary>
        public sealed record DroppingStop(string TenantName, string SessionId, DateTimeOffset StoppedAt);

        /// <summary>The <c>QueuedAt</c> and tenant of a stored <c>WkfloQueueMessage&lt;AgentEventMessage&gt;</c>.</summary>
        public sealed class QueuedMessageStamp
        {
            public DateTimeOffset? QueuedAt { get; set; }

            public StampTenant TenantContext { get; set; }
        }

        public sealed class StampTenant
        {
            public string TenantName { get; set; }
        }
    }
}
