using AMAIS.WKFLO.Common.Models;
using Dapr;
using Dapr.Client;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop
{
    /// <summary>
    /// US 7501: durable copy of the last Stop of a session (<c>chat-stop-{tenant}:{sessionId}</c> in the AI state store). The
    /// broadcast can be missed (e.g. a replica that is not subscribed yet), so turns also read this key once at checkpoints they already pass (see
    /// <see cref="IChatStopCheckpoint"/>) in case a replica missed the broadcast.
    /// </summary>
    public interface IChatStopStateStore
    {
        /// <summary>
        /// Records the Stop. Monotonic: a Stop never moves the stored time backwards (two Stops of the same session racing,
        /// or a slow replica, keep the latest). Throws when the write keeps conflicting.
        /// </summary>
        Task SetStoppedAsync(string tenantName, string sessionId, DateTimeOffset stoppedAt, CancellationToken cancellationToken = default);

        Task<DateTimeOffset?> GetStoppedAtAsync(string tenantName, string sessionId, CancellationToken cancellationToken = default);
    }

    public class ChatStopState
    {
        public DateTimeOffset StoppedAt { get; set; }
    }

    public class ChatStopStateStore : IChatStopStateStore
    {
        /// <summary>Attempts of the read-compare-write before giving up (each conflict means another Stop just wrote).</summary>
        internal const int MaxWriteAttempts = 5;

        private readonly DaprClient _daprClient;

        public ChatStopStateStore(DaprClient daprClient)
        {
            _daprClient = daprClient;
        }

        public async Task SetStoppedAsync(string tenantName, string sessionId, DateTimeOffset stoppedAt, CancellationToken cancellationToken = default)
        {
            var key = ChatStopConsts.BuildStateKey(tenantName, sessionId);
            var metadata = new Dictionary<string, string> { ["ttlInSeconds"] = ChatStopConsts.StateTtlSeconds.ToString() };

            for (var attempt = 0; attempt < MaxWriteAttempts; attempt++)
            {
                var (current, etag) = await _daprClient.GetStateAndETagAsync<ChatStopState>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, key, cancellationToken: cancellationToken);

                // Already at this Stop or a later one: nothing to do.
                if (current != null && current.StoppedAt >= stoppedAt)
                    return;

                // Written only if nobody wrote since the read (the ETag; first-write concurrency when the key is absent).
                bool saved;
                try
                {
                    saved = await _daprClient.TrySaveStateAsync(
                        CommonConsts.CommonAiPubSubConsts.StateStoreName,
                        key,
                        new ChatStopState { StoppedAt = stoppedAt },
                        etag ?? string.Empty,
                        new StateOptions { Concurrency = ConcurrencyMode.FirstWrite },
                        metadata,
                        cancellationToken);
                }
                catch (DaprException) when (string.IsNullOrEmpty(etag))
                {
                    // The key was absent and a concurrent first Stop wrote it first: Dapr reports that insert-only conflict
                    // as an error (the SDK throws instead of returning false). Re-read and compare again.
                    continue;
                }

                if (saved)
                    return;
            }

            throw new InvalidOperationException($"Recording the stop of session {sessionId} did not succeed after {MaxWriteAttempts} attempts");
        }

        public async Task<DateTimeOffset?> GetStoppedAtAsync(string tenantName, string sessionId, CancellationToken cancellationToken = default)
        {
            var state = await _daprClient.GetStateAsync<ChatStopState>(
                CommonConsts.CommonAiPubSubConsts.StateStoreName,
                ChatStopConsts.BuildStateKey(tenantName, sessionId),
                cancellationToken: cancellationToken);

            return state?.StoppedAt;
        }
    }
}
