using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Models;
using Dapr;
using Dapr.Client;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatStop
{
    /// <summary>US 7501: the stored Stop time only ever moves forward, and keeps its TTL.</summary>
    public class ChatStopStateStoreTests
    {
        private const string Key = "chat-stop-tenant-a:session-1";
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<DaprClient> _dapr = new();
        private readonly ChatStopStateStore _store;

        public ChatStopStateStoreTests()
        {
            _store = new ChatStopStateStore(_dapr.Object);
        }

        private void Stored(params (DateTimeOffset? StoppedAt, string ETag)[] reads)
        {
            var sequence = _dapr.SetupSequence(x => x.GetStateAndETagAsync<ChatStopState>(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key,
                It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()));
            foreach (var (stoppedAt, etag) in reads)
                sequence = sequence.ReturnsAsync((stoppedAt is { } at ? new ChatStopState { StoppedAt = at } : null, etag));
        }

        private void SaveResults(params bool[] results)
        {
            var sequence = _dapr.SetupSequence(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ChatStopState>(),
                It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()));
            foreach (var result in results)
                sequence = sequence.ReturnsAsync(result);
        }

        private void VerifySaves(Func<Times> times) =>
            _dapr.Verify(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ChatStopState>(),
                It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), times);

        [Fact]
        public async Task SetStopped_NoStopYet_WritesItWithTheTtl()
        {
            Stored((null, ""));
            SaveResults(true);

            await _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key,
                It.Is<ChatStopState>(s => s.StoppedAt == StoppedAt), "", It.Is<StateOptions>(o => o.Concurrency == ConcurrencyMode.FirstWrite),
                It.Is<IReadOnlyDictionary<string, string>>(m => m["ttlInSeconds"] == ChatStopConsts.StateTtlSeconds.ToString()), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetStopped_EarlierStopStored_ReplacesItUsingItsETag()
        {
            Stored((StoppedAt.AddSeconds(-30), "etag-1"));
            SaveResults(true);

            await _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key,
                It.Is<ChatStopState>(s => s.StoppedAt == StoppedAt), "etag-1", It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        public async Task SetStopped_SameOrLaterStopStored_NeverMovesItBackwards(int storedSecondsLater)
        {
            Stored((StoppedAt.AddSeconds(storedSecondsLater), "etag-1"));

            await _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken);

            VerifySaves(Times.Never);
        }

        [Fact]
        public async Task SetStopped_ConflictWithALaterStop_RereadsAndStops()
        {
            Stored((StoppedAt.AddSeconds(-30), "etag-1"), (StoppedAt.AddSeconds(2), "etag-2"));
            SaveResults(false);

            await _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken);

            VerifySaves(Times.Once);
        }

        [Fact]
        public async Task SetStopped_ConflictWithAnEarlierWrite_RetriesWithTheNewETag()
        {
            Stored((StoppedAt.AddSeconds(-30), "etag-1"), (StoppedAt.AddSeconds(-10), "etag-2"));
            SaveResults(false, true);

            await _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ChatStopState>(), "etag-2",
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        private static DaprException FirstWriteConflict() =>
            new("State operation failed: the Dapr endpoint indicated a failure.", new InvalidOperationException("possible etag mismatch"));

        /// <summary>
        /// Review fix: two first Stops racing on an absent key. The insert-only write that loses throws (Dapr reports the
        /// conflict as an error) instead of returning false; the store re-reads and keeps the later Stop.
        /// </summary>
        [Fact]
        public async Task SetStopped_AbsentKeyWriteLosesToALaterStop_RereadsAndKeepsTheLaterOne()
        {
            Stored((null, ""), (StoppedAt.AddSeconds(2), "etag-2"));
            _dapr.Setup(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ChatStopState>(),
                    "", It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(FirstWriteConflict());

            await _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken);

            VerifySaves(Times.Once);
        }

        [Fact]
        public async Task SetStopped_AbsentKeyWriteLosesToAnEarlierStop_RereadsAndReplacesIt()
        {
            Stored((null, ""), (StoppedAt.AddSeconds(-2), "etag-2"));
            _dapr.Setup(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ChatStopState>(),
                    "", It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(FirstWriteConflict());
            _dapr.Setup(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ChatStopState>(),
                    "etag-2", It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            await _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.Is<ChatStopState>(s => s.StoppedAt == StoppedAt),
                "etag-2", It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetStopped_WriteWithAnETagFails_IsNotRetried()
        {
            Stored((StoppedAt.AddSeconds(-30), "etag-1"));
            _dapr.Setup(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ChatStopState>(),
                    "etag-1", It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DaprException("state store down"));

            await Assert.ThrowsAsync<DaprException>(() => _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SetStopped_KeepsConflicting_GivesUpAfterTheBoundedAttempts()
        {
            _dapr.Setup(x => x.GetStateAndETagAsync<ChatStopState>(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key,
                    It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new ChatStopState { StoppedAt = StoppedAt.AddSeconds(-30) }, "etag"));
            _dapr.Setup(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ChatStopState>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SetStoppedAsync("tenant-a", "session-1", StoppedAt, TestContext.Current.CancellationToken));

            VerifySaves(() => Times.Exactly(ChatStopStateStore.MaxWriteAttempts));
        }
    }
}
