using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop
{
    /// <summary>
    /// US 7501: one chat turn running on this replica. Disposing it unregisters the turn.
    /// </summary>
    public interface IChatTurn : IDisposable
    {
        /// <summary>The tenant the turn runs for. A Stop only reaches turns of its own tenant.</summary>
        string TenantName { get; }

        string SessionId { get; }

        /// <summary>When the turn was requested (the message's queue time, or the activity start when unknown).</summary>
        DateTimeOffset StartedAt { get; }

        /// <summary>Cancelled when the user stops the session.</summary>
        CancellationToken Token { get; }

        /// <summary>True once <see cref="Token"/> was cancelled because of a Stop (and not, e.g., a timeout).</summary>
        bool StopRequested { get; }
    }

    /// <summary>
    /// US 7501: the chat turns this replica is running right now, by (tenant, session). A Stop reaches every replica (see
    /// <see cref="ChatStopBroadcastHandler"/>) and each one cancels its own matching turns here. No timers or polling:
    /// one add when a turn starts and one remove when it ends.
    /// </summary>
    public interface IChatTurnRegistry
    {
        IChatTurn Register(string tenantName, string sessionId, DateTimeOffset startedAt);

        /// <summary>
        /// US 7506: same as <see cref="Register(string, string, DateTimeOffset)"/> for the turn of a queued hand-off:
        /// <paramref name="queuedWorkId"/> (the hand-off's entry in the session working state) is removed when this turn ends.
        /// </summary>
        IChatTurn Register(string tenantName, string sessionId, DateTimeOffset startedAt, string queuedWorkId);

        /// <summary>
        /// Cancels the turns of that tenant's session that started before <paramref name="stoppedAt"/>. Turns of the same
        /// session id in another tenant are never touched. Returns how many were cancelled.
        /// </summary>
        int CancelTurnsStartedBefore(string tenantName, string sessionId, DateTimeOffset stoppedAt);

        /// <summary>
        /// Whether this replica runs a turn of that tenant's session that started after <paramref name="startedAt"/> and
        /// was not stopped: a new message sent after the Stop is already running.
        /// </summary>
        bool HasRunningTurnStartedAfter(string tenantName, string sessionId, DateTimeOffset startedAt);
    }

    /// <summary>
    /// Singleton, thread safe. US 7506: every turn is also an entry of its session's shared working state
    /// (<see cref="ISessionWorkingStateStore"/>), added when it registers and removed when it ends. Those writes run in the
    /// background, in order per turn, and never fail the turn.
    /// </summary>
    public sealed class ChatTurnRegistry : IChatTurnRegistry
    {
        private readonly ConcurrentDictionary<string, ImmutableList<ChatTurn>> _turns = new(StringComparer.Ordinal);
        private readonly ILogger<ChatTurnRegistry> _logger;
        private readonly ISessionWorkingStateStore _workingState;

        public ChatTurnRegistry() : this(NullLogger<ChatTurnRegistry>.Instance)
        {
        }

        public ChatTurnRegistry(ILogger<ChatTurnRegistry> logger) : this(logger, null)
        {
        }

        public ChatTurnRegistry(ILogger<ChatTurnRegistry> logger, ISessionWorkingStateStore workingState)
        {
            _logger = logger ?? NullLogger<ChatTurnRegistry>.Instance;
            _workingState = workingState;
        }

        public IChatTurn Register(string tenantName, string sessionId, DateTimeOffset startedAt) =>
            Register(tenantName, sessionId, startedAt, queuedWorkId: null);

        public IChatTurn Register(string tenantName, string sessionId, DateTimeOffset startedAt, string queuedWorkId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            var turn = new ChatTurn(this, tenantName, sessionId, startedAt, queuedWorkId);
            _turns.AddOrUpdate(turn.Scope, _ => ImmutableList.Create(turn), (_, existing) => existing.Add(turn));

            if (_workingState != null)
                turn.WorkingStateWrite = _workingState.MarkWorkingAsync(tenantName, sessionId, turn.WorkId, startedAt, SessionWorkingStateConsts.TurnTimeToLive);

            return turn;
        }

        public int CancelTurnsStartedBefore(string tenantName, string sessionId, DateTimeOffset stoppedAt)
        {
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(sessionId)
                || !_turns.TryGetValue(ChatStopConsts.BuildScope(tenantName, sessionId), out var turns))
                return 0;

            var cancelled = 0;
            foreach (var turn in turns)
            {
                if (turn.StartedAt < stoppedAt && turn.Stop())
                {
                    cancelled++;
                    _logger.LogDebug("Stop cancelled a turn of session {SessionId} (tenant {TenantName}): turn started at {StartedAt}, stopped at {StoppedAt}",
                        sessionId, tenantName, turn.StartedAt, stoppedAt);
                }
            }

            return cancelled;
        }

        public bool HasRunningTurnStartedAfter(string tenantName, string sessionId, DateTimeOffset startedAt) =>
            !string.IsNullOrWhiteSpace(tenantName) && !string.IsNullOrWhiteSpace(sessionId)
            && _turns.TryGetValue(ChatStopConsts.BuildScope(tenantName, sessionId), out var turns)
            && turns.Any(t => t.StartedAt > startedAt && !t.StopRequested);

        internal int CountFor(string tenantName, string sessionId) =>
            _turns.TryGetValue(ChatStopConsts.BuildScope(tenantName, sessionId), out var turns) ? turns.Count : 0;

        private void Unregister(ChatTurn turn)
        {
            RemoveFromRegistry(turn);

            // After the turn's own entry was written, so a quick turn can never leave its entry behind.
            if (_workingState != null)
            {
                var workIds = turn.QueuedWorkId == null ? new[] { turn.WorkId } : new[] { turn.WorkId, turn.QueuedWorkId };
                _ = turn.WorkingStateWrite
                    .ContinueWith(_ => _workingState.ClearWorkingAsync(turn.TenantName, turn.SessionId, workIds), TaskScheduler.Default)
                    .Unwrap();
            }
        }

        private void RemoveFromRegistry(ChatTurn turn)
        {
            while (_turns.TryGetValue(turn.Scope, out var existing))
            {
                var updated = existing.Remove(turn);
                if (ReferenceEquals(updated, existing))
                    return;

                // Compare-and-swap on the exact list read above, so a turn registered meanwhile is never lost.
                var swapped = updated.IsEmpty
                    ? _turns.TryRemove(new KeyValuePair<string, ImmutableList<ChatTurn>>(turn.Scope, existing))
                    : _turns.TryUpdate(turn.Scope, updated, existing);

                if (swapped)
                    return;
            }
        }

        private sealed class ChatTurn : IChatTurn
        {
            private readonly ChatTurnRegistry _owner;
            private readonly CancellationTokenSource _cts = new();
            private int _stopRequested;
            private int _disposed;

            public ChatTurn(ChatTurnRegistry owner, string tenantName, string sessionId, DateTimeOffset startedAt, string queuedWorkId)
            {
                _owner = owner;
                QueuedWorkId = queuedWorkId;
                TenantName = tenantName;
                SessionId = sessionId;
                Scope = ChatStopConsts.BuildScope(tenantName, sessionId);
                StartedAt = startedAt;
                Token = _cts.Token;
            }

            public string TenantName { get; }
            public string SessionId { get; }
            public string Scope { get; }
            public DateTimeOffset StartedAt { get; }
            public CancellationToken Token { get; }
            public bool StopRequested => Volatile.Read(ref _stopRequested) == 1;

            /// <summary>This turn's entry in the session working state.</summary>
            public string WorkId { get; } = $"turn:{Guid.NewGuid():N}";

            /// <summary>The queued hand-off's entry this turn takes over, if any.</summary>
            public string QueuedWorkId { get; }

            /// <summary>The write of <see cref="WorkId"/>; the clear waits for it.</summary>
            public Task WorkingStateWrite { get; set; } = Task.CompletedTask;

            /// <summary>Returns true only for the call that actually stopped this turn.</summary>
            public bool Stop()
            {
                if (Interlocked.CompareExchange(ref _stopRequested, 1, 0) != 0)
                    return false;

                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The turn ended at the same moment; nothing left to cancel.
                }

                return true;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                _owner.Unregister(this);
                _cts.Dispose();
            }
        }
    }
}
