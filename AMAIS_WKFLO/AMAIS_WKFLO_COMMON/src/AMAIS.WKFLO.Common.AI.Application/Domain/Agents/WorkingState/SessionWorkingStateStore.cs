using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Models;
using Dapr;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState
{
    /// <summary>
    /// US 7506: whether any agent is working on a chat session right now, shared by every replica, so a chat the user
    /// left and comes back to can show that work is still going on (typing, Agents bar, Stop).
    /// <para>
    /// One Dapr state key per session, <c>chat-working-{tenant}:{sessionId}</c>, holding one entry per unit of work: each
    /// running turn (added and removed by <see cref="ChatTurnRegistry"/>) and each hand-off waiting in an agent's queue
    /// (added when it is published, removed when its turn ends). Every entry expires on its own and the key has a TTL, so
    /// a replica that dies mid-turn cannot leave the session "working" for good.
    /// </para>
    /// <para>Writes are best effort: they never throw, because the chat must work even when this state cannot be written.</para>
    /// </summary>
    public interface ISessionWorkingStateStore
    {
        Task MarkWorkingAsync(string tenantName, string sessionId, string workId, DateTimeOffset startedAt, TimeSpan timeToLive);

        Task ClearWorkingAsync(string tenantName, string sessionId, IReadOnlyCollection<string> workIds);

        /// <summary>
        /// True when the session has unexpired work that started at or after <paramref name="stoppedAt"/> (work started
        /// before the session's last Stop is being cancelled, so it no longer counts).
        /// </summary>
        Task<bool> IsWorkingAsync(string tenantName, string sessionId, DateTimeOffset? stoppedAt, CancellationToken cancellationToken = default);
    }

    public static class SessionWorkingStateConsts
    {
        public const string StateKeyPrefix = "chat-working-";

        /// <summary>A turn still running after 5 minutes is already treated as stale elsewhere; 10 is generous.</summary>
        public static readonly TimeSpan TurnTimeToLive = TimeSpan.FromMinutes(10);

        /// <summary>A queued hand-off waits for the agent's earlier hand-offs (up to 5 minutes each), then runs.</summary>
        public static readonly TimeSpan HandOffTimeToLive = TimeSpan.FromMinutes(15);

        public static string BuildStateKey(string tenantName, string sessionId) => $"{StateKeyPrefix}{ChatStopConsts.BuildScope(tenantName, sessionId)}";
    }

    public class SessionWorkingState
    {
        public Dictionary<string, SessionWorkItem> Items { get; set; } = new(StringComparer.Ordinal);
    }

    public class SessionWorkItem
    {
        /// <summary>When the work was requested; compared with the session's last Stop.</summary>
        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <summary>Singleton, thread safe (stateless apart from the Dapr client).</summary>
    public class SessionWorkingStateStore : ISessionWorkingStateStore
    {
        /// <summary>Attempts of the read-modify-write before giving up (each conflict means another write just landed).</summary>
        internal const int MaxWriteAttempts = 5;

        private readonly DaprClient _daprClient;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<SessionWorkingStateStore> _logger;

        public SessionWorkingStateStore(DaprClient daprClient, ILogger<SessionWorkingStateStore> logger)
            : this(daprClient, TimeProvider.System, logger)
        {
        }

        internal SessionWorkingStateStore(DaprClient daprClient, TimeProvider timeProvider, ILogger<SessionWorkingStateStore> logger)
        {
            _daprClient = daprClient;
            _timeProvider = timeProvider;
            _logger = logger ?? NullLogger<SessionWorkingStateStore>.Instance;
        }

        public Task MarkWorkingAsync(string tenantName, string sessionId, string workId, DateTimeOffset startedAt, TimeSpan timeToLive) =>
            UpdateAsync(tenantName, sessionId, (state, now) =>
            {
                state.Items[workId] = new SessionWorkItem { StartedAt = startedAt, ExpiresAt = now.Add(timeToLive) };
                return true;
            });

        public Task ClearWorkingAsync(string tenantName, string sessionId, IReadOnlyCollection<string> workIds) =>
            UpdateAsync(tenantName, sessionId, (state, _) =>
            {
                var removed = false;
                foreach (var workId in workIds)
                    removed |= state.Items.Remove(workId);
                return removed;
            });

        public async Task<bool> IsWorkingAsync(string tenantName, string sessionId, DateTimeOffset? stoppedAt, CancellationToken cancellationToken = default)
        {
            var state = await _daprClient.GetStateAsync<SessionWorkingState>(
                CommonConsts.CommonAiPubSubConsts.StateStoreName,
                SessionWorkingStateConsts.BuildStateKey(tenantName, sessionId),
                cancellationToken: cancellationToken);

            var now = _timeProvider.GetUtcNow();
            return state?.Items?.Values.Any(i => i.ExpiresAt > now && (stoppedAt == null || i.StartedAt >= stoppedAt)) == true;
        }

        /// <param name="change">Applies the change to the stored state; returns false when nothing changed.</param>
        private async Task UpdateAsync(string tenantName, string sessionId, Func<SessionWorkingState, DateTimeOffset, bool> change)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            var key = SessionWorkingStateConsts.BuildStateKey(tenantName, sessionId);

            try
            {
                for (var attempt = 0; attempt < MaxWriteAttempts; attempt++)
                {
                    var (state, etag) = await _daprClient.GetStateAndETagAsync<SessionWorkingState>(CommonConsts.CommonAiPubSubConsts.StateStoreName, key);
                    state ??= new SessionWorkingState();
                    state.Items ??= new Dictionary<string, SessionWorkItem>(StringComparer.Ordinal);

                    var now = _timeProvider.GetUtcNow();
                    var expired = state.Items.Where(i => i.Value.ExpiresAt <= now).Select(i => i.Key).ToList();
                    foreach (var expiredKey in expired)
                        state.Items.Remove(expiredKey);

                    if (!change(state, now) && expired.Count == 0)
                        return;

                    bool saved;
                    try
                    {
                        saved = state.Items.Count == 0
                            ? await _daprClient.TryDeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key, etag ?? string.Empty,
                                new StateOptions { Concurrency = ConcurrencyMode.FirstWrite })
                            : await _daprClient.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key, state, etag ?? string.Empty,
                                new StateOptions { Concurrency = ConcurrencyMode.FirstWrite }, BuildTtlMetadata(state, now));
                    }
                    catch (DaprException) when (string.IsNullOrEmpty(etag))
                    {
                        // The key was absent and a concurrent first write created it (Dapr reports that as an error). Re-read.
                        continue;
                    }

                    if (saved)
                        return;
                }

                _logger.LogWarning("Updating the working state of session {SessionId} kept conflicting; giving up", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Updating the working state of session {SessionId} failed", sessionId);
            }
        }

        /// <summary>The key lives as long as its longest-lived entry.</summary>
        private static Dictionary<string, string> BuildTtlMetadata(SessionWorkingState state, DateTimeOffset now)
        {
            var ttl = state.Items.Values.Max(i => i.ExpiresAt) - now;
            var seconds = Math.Max(1, (int)Math.Ceiling(ttl.TotalSeconds));
            return new Dictionary<string, string> { ["ttlInSeconds"] = seconds.ToString() };
        }
    }
}
