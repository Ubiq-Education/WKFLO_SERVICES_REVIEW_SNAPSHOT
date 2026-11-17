using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.Models;
using Dapr;
using Dapr.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.WorkingState
{
    /// <summary>US 7506: the shared "session is working" state.</summary>
    public class SessionWorkingStateStoreTests
    {
        private const string Key = "chat-working-tenant-a:session-1";
        private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<DaprClient> _dapr = new();
        private readonly SessionWorkingStateStore _store;
        private SessionWorkingState _saved;

        public SessionWorkingStateStoreTests()
        {
            _store = new SessionWorkingStateStore(_dapr.Object, new FixedTimeProvider(Now), NullLogger<SessionWorkingStateStore>.Instance);
        }

        private void Stored(SessionWorkingState state, string etag = "etag-1") =>
            _dapr.Setup(x => x.GetStateAndETagAsync<SessionWorkingState>(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key,
                    It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((state, etag));

        private void SaveSucceeds() =>
            _dapr.Setup(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<SessionWorkingState>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, SessionWorkingState, string, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, state, _, _, _, _) => _saved = state)
                .ReturnsAsync(true);

        private static SessionWorkingState With(params (string Id, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt)[] items)
        {
            var state = new SessionWorkingState();
            foreach (var (id, startedAt, expiresAt) in items)
                state.Items[id] = new SessionWorkItem { StartedAt = startedAt, ExpiresAt = expiresAt };
            return state;
        }

        [Fact]
        public async Task MarkWorking_AddsTheEntry_WithItsExpiry_AndTheKeyTtl()
        {
            Stored(null, etag: "");
            SaveSucceeds();

            await _store.MarkWorkingAsync("tenant-a", "session-1", "turn:1", Now.AddSeconds(-5), TimeSpan.FromMinutes(10));

            var item = _saved.Items["turn:1"];
            Assert.Equal(Now.AddSeconds(-5), item.StartedAt);
            Assert.Equal(Now.AddMinutes(10), item.ExpiresAt);
            _dapr.Verify(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<SessionWorkingState>(), "",
                It.Is<StateOptions>(o => o.Concurrency == ConcurrencyMode.FirstWrite),
                It.Is<IReadOnlyDictionary<string, string>>(m => m["ttlInSeconds"] == "600"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task MarkWorking_KeepsOtherEntries_AndDropsExpiredOnes()
        {
            Stored(With(("turn:other", Now, Now.AddMinutes(5)), ("turn:dead", Now.AddMinutes(-20), Now.AddMinutes(-1))));
            SaveSucceeds();

            await _store.MarkWorkingAsync("tenant-a", "session-1", "turn:1", Now, TimeSpan.FromMinutes(10));

            Assert.Equal(["turn:other", "turn:1"], _saved.Items.Keys);
        }

        [Fact]
        public async Task MarkWorking_RetriesOnAConflict()
        {
            Stored(With(("turn:other", Now, Now.AddMinutes(5))));
            _dapr.SetupSequence(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<SessionWorkingState>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false)
                .ReturnsAsync(true);

            await _store.MarkWorkingAsync("tenant-a", "session-1", "turn:1", Now, TimeSpan.FromMinutes(10));

            _dapr.Verify(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<SessionWorkingState>(),
                It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task MarkWorking_DaprFails_DoesNotThrow()
        {
            _dapr.Setup(x => x.GetStateAndETagAsync<SessionWorkingState>(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DaprException("sidecar down"));

            await _store.MarkWorkingAsync("tenant-a", "session-1", "turn:1", Now, TimeSpan.FromMinutes(10));
        }

        [Fact]
        public async Task ClearWorking_RemovesOnlyTheGivenEntries()
        {
            Stored(With(("turn:1", Now, Now.AddMinutes(5)), ("msg:queued", Now, Now.AddMinutes(5)), ("turn:other", Now, Now.AddMinutes(5))));
            SaveSucceeds();

            await _store.ClearWorkingAsync("tenant-a", "session-1", ["turn:1", "msg:queued"]);

            Assert.Equal(["turn:other"], _saved.Items.Keys);
        }

        [Fact]
        public async Task ClearWorking_LastEntry_DeletesTheKey()
        {
            Stored(With(("turn:1", Now, Now.AddMinutes(5))));

            await _store.ClearWorkingAsync("tenant-a", "session-1", ["turn:1"]);

            _dapr.Verify(x => x.TryDeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, "etag-1", It.IsAny<StateOptions>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ClearWorking_NothingToRemove_WritesNothing()
        {
            Stored(With(("turn:other", Now, Now.AddMinutes(5))));

            await _store.ClearWorkingAsync("tenant-a", "session-1", ["turn:1"]);

            _dapr.Verify(x => x.TrySaveStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SessionWorkingState>(),
                It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private void Read(SessionWorkingState state) =>
            _dapr.Setup(x => x.GetStateAsync<SessionWorkingState>(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key,
                    It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(state);

        [Fact]
        public async Task IsWorking_NoState_IsFalse()
        {
            Read(null);

            Assert.False(await _store.IsWorkingAsync("tenant-a", "session-1", null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task IsWorking_UnexpiredEntry_IsTrue()
        {
            Read(With(("turn:1", Now.AddMinutes(-1), Now.AddMinutes(5))));

            Assert.True(await _store.IsWorkingAsync("tenant-a", "session-1", null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task IsWorking_OnlyExpiredEntries_IsFalse()
        {
            Read(With(("turn:1", Now.AddMinutes(-20), Now.AddSeconds(-1))));

            Assert.False(await _store.IsWorkingAsync("tenant-a", "session-1", null, TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(-10, false)] // requested before the Stop: being cancelled
        [InlineData(0, true)]    // a turn started at the Stop instant is not cancelled by it
        [InlineData(10, true)]   // the next message, after the Stop
        public async Task IsWorking_IgnoresWorkRequestedBeforeTheLastStop(int startedSecondsAfterStop, bool expected)
        {
            var stoppedAt = Now.AddMinutes(-1);
            Read(With(("turn:1", stoppedAt.AddSeconds(startedSecondsAfterStop), Now.AddMinutes(5))));

            Assert.Equal(expected, await _store.IsWorkingAsync("tenant-a", "session-1", stoppedAt, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void BuildStateKey_IsScopedByTheNormalizedTenant()
        {
            Assert.Equal(Key, SessionWorkingStateConsts.BuildStateKey(" Tenant-A ", "session-1"));
        }

        private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }
}
