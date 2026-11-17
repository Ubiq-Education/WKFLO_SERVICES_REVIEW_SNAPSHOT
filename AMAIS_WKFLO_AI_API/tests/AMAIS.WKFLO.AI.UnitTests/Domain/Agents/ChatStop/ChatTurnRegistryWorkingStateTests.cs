using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatStop
{
    /// <summary>US 7506: every registered turn is an entry of its session's shared working state while it runs.</summary>
    public class ChatTurnRegistryWorkingStateTests
    {
        private static readonly DateTimeOffset StartedAt = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        private readonly RecordingWorkingStateStore _store = new();
        private readonly ChatTurnRegistry _registry;

        public ChatTurnRegistryWorkingStateTests()
        {
            _registry = new ChatTurnRegistry(NullLogger<ChatTurnRegistry>.Instance, _store);
        }

        [Fact]
        public void Register_MarksTheSessionWorking_FromTheTurnsStart()
        {
            using var turn = _registry.Register("tenant-a", "session-1", StartedAt);

            var mark = Assert.Single(_store.Marks);
            Assert.Equal(("tenant-a", "session-1", StartedAt, SessionWorkingStateConsts.TurnTimeToLive), (mark.TenantName, mark.SessionId, mark.StartedAt, mark.TimeToLive));
            Assert.StartsWith("turn:", mark.WorkId);
        }

        [Fact]
        public async Task Dispose_ClearsTheTurnsEntry()
        {
            var turn = _registry.Register("tenant-a", "session-1", StartedAt);
            var workId = _store.Marks.Single().WorkId;

            turn.Dispose();

            var clear = await _store.NextClearAsync();
            Assert.Equal(("tenant-a", "session-1"), (clear.TenantName, clear.SessionId));
            Assert.Equal([workId], clear.WorkIds);
        }

        [Fact]
        public async Task Dispose_OfAHandOffTurn_AlsoClearsTheQueuedEntry()
        {
            var turn = _registry.Register("tenant-a", "session-1", StartedAt, queuedWorkId: "msg:abc");
            var workId = _store.Marks.Single().WorkId;

            turn.Dispose();

            var clear = await _store.NextClearAsync();
            Assert.Equal([workId, "msg:abc"], clear.WorkIds);
        }

        [Fact]
        public async Task Dispose_WaitsForTheMarkWrite_SoAQuickTurnLeavesNothingBehind()
        {
            var markWrite = new TaskCompletionSource();
            _store.NextMark = markWrite.Task;
            var turn = _registry.Register("tenant-a", "session-1", StartedAt);

            turn.Dispose();

            // The clear is a continuation of the pending mark write: it cannot have run yet.
            Assert.Empty(_store.Clears);

            markWrite.SetResult();

            await _store.NextClearAsync();
        }

        [Fact]
        public void WithoutAStore_TurnsStillWork()
        {
            var registry = new ChatTurnRegistry();

            using var turn = registry.Register("tenant-a", "session-1", StartedAt);

            Assert.Equal(1, registry.CountFor("tenant-a", "session-1"));
        }

        private sealed class RecordingWorkingStateStore : ISessionWorkingStateStore
        {
            private readonly SemaphoreSlim _clearSignal = new(0);

            public ConcurrentQueue<(string TenantName, string SessionId, string WorkId, DateTimeOffset StartedAt, TimeSpan TimeToLive)> Marks { get; } = new();
            public ConcurrentQueue<(string TenantName, string SessionId, IReadOnlyCollection<string> WorkIds)> Clears { get; } = new();
            public Task NextMark { get; set; } = Task.CompletedTask;

            public Task MarkWorkingAsync(string tenantName, string sessionId, string workId, DateTimeOffset startedAt, TimeSpan timeToLive)
            {
                Marks.Enqueue((tenantName, sessionId, workId, startedAt, timeToLive));
                return NextMark;
            }

            public Task ClearWorkingAsync(string tenantName, string sessionId, IReadOnlyCollection<string> workIds)
            {
                Clears.Enqueue((tenantName, sessionId, workIds));
                _clearSignal.Release();
                return Task.CompletedTask;
            }

            public Task<bool> IsWorkingAsync(string tenantName, string sessionId, DateTimeOffset? stoppedAt, CancellationToken cancellationToken = default) =>
                Task.FromResult(false);

            public async Task<(string TenantName, string SessionId, IReadOnlyCollection<string> WorkIds)> NextClearAsync()
            {
                Assert.True(await _clearSignal.WaitAsync(TimeSpan.FromSeconds(5)), "the turn's working-state entry was never cleared");
                Assert.True(Clears.TryPeek(out var clear));
                return clear;
            }
        }
    }
}
