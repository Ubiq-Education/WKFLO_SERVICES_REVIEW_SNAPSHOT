using AMAIS.WKFLO.AI.Application.Agents.Actors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Models;
using Dapr.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Actors
{
    /// <summary>US 7501: the actor's dequeue checkpoint and the reads behind ClearQueue.</summary>
    public class HandOffQueueStopGuardTests
    {
        private const string ActorId = "session-1:7";
        private const string Key = "msg:instance-1";
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<IChatStopStateStore> _stopStateStore = new();
        private readonly Mock<DaprClient> _daprClient = new();
        private readonly HandOffQueueStopGuard _guard;

        public HandOffQueueStopGuardTests()
        {
            _guard = new HandOffQueueStopGuard(_stopStateStore.Object, _daprClient.Object, NullLogger<HandOffQueueStopGuard>.Instance);
        }

        private void SetupStoppedAt(DateTimeOffset? stoppedAt, string tenantName = "tenant-a") =>
            _stopStateStore.Setup(x => x.GetStoppedAtAsync(tenantName, "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(stoppedAt);

        private void SetupQueuedAt(string key, DateTimeOffset? queuedAt, string tenantName = "tenant-a") =>
            _daprClient
                .Setup(x => x.GetStateAsync<HandOffQueueStopGuard.QueuedMessageStamp>(CommonConsts.CommonAiPubSubConsts.StateStoreName, key,
                    It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HandOffQueueStopGuard.QueuedMessageStamp
                {
                    QueuedAt = queuedAt,
                    TenantContext = new HandOffQueueStopGuard.StampTenant { TenantName = tenantName }
                });

        private void VerifyQueuedAtRead(Times times) =>
            _daprClient.Verify(x => x.GetStateAsync<HandOffQueueStopGuard.QueuedMessageStamp>(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), times);

        [Fact]
        public async Task IsStoppedAsync_HandOffQueuedBeforeTheStop_True()
        {
            SetupStoppedAt(StoppedAt);
            SetupQueuedAt(Key, StoppedAt.AddSeconds(-5));

            Assert.True(await _guard.IsStoppedAsync(ActorId, Key, TestContext.Current.CancellationToken));
            _stopStateStore.Verify(x => x.GetStoppedAtAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>US 7502: the dequeue check also says which Stop dropped the hand-off (to save its marker).</summary>
        [Fact]
        public async Task GetDroppingStopAsync_HandOffQueuedBeforeTheStop_ReturnsTheStop()
        {
            SetupStoppedAt(StoppedAt);
            SetupQueuedAt(Key, StoppedAt.AddSeconds(-5));

            var stop = await _guard.GetDroppingStopAsync(ActorId, Key, TestContext.Current.CancellationToken);

            Assert.Equal(new HandOffQueueStopGuard.DroppingStop("tenant-a", "session-1", StoppedAt), stop);
        }

        /// <summary>US 7501 (review fix): a Stop of another tenant on the same session id never drops this hand-off.</summary>
        [Fact]
        public async Task IsStoppedAsync_StopOfAnotherTenant_False()
        {
            SetupStoppedAt(StoppedAt, tenantName: "tenant-b");
            SetupQueuedAt(Key, StoppedAt.AddSeconds(-5), tenantName: "tenant-a");

            Assert.False(await _guard.IsStoppedAsync(ActorId, Key, TestContext.Current.CancellationToken));
            _stopStateStore.Verify(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>()), Times.Once);
            _stopStateStore.Verify(x => x.GetStoppedAtAsync("tenant-b", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task IsStoppedAsync_HandOffWithoutATenant_NeverReadsAStopState()
        {
            SetupQueuedAt(Key, StoppedAt.AddSeconds(-5), tenantName: null);

            Assert.False(await _guard.IsStoppedAsync(ActorId, Key, TestContext.Current.CancellationToken));
            _stopStateStore.Verify(x => x.GetStoppedAtAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task IsStoppedAsync_HandOffQueuedAfterTheStop_False()
        {
            // The person's next message after the Stop hands off again: that hand-off must run.
            SetupStoppedAt(StoppedAt);
            SetupQueuedAt(Key, StoppedAt.AddSeconds(5));

            Assert.False(await _guard.IsStoppedAsync(ActorId, Key, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task IsStoppedAsync_SessionNeverStopped_ReadsTheHandOffAndTheStopStateOnce()
        {
            // The common case costs two single reads: the hand-off (its tenant decides which stop state applies) and the stop state.
            SetupStoppedAt(null);
            SetupQueuedAt(Key, StoppedAt.AddSeconds(-5));

            Assert.False(await _guard.IsStoppedAsync(ActorId, Key, TestContext.Current.CancellationToken));
            VerifyQueuedAtRead(Times.Once());
            _stopStateStore.Verify(x => x.GetStoppedAtAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task IsStoppedAsync_StopStateReadFails_DoesNotBlockTheHandOff()
        {
            SetupQueuedAt(Key, StoppedAt.AddSeconds(-5));
            _stopStateStore.Setup(x => x.GetStoppedAtAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("state store down"));

            Assert.False(await _guard.IsStoppedAsync(ActorId, Key, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task IsStoppedAsync_NotAnAgentActorId_False()
        {
            Assert.False(await _guard.IsStoppedAsync("not-an-actor-id", Key, TestContext.Current.CancellationToken));
            _stopStateStore.Verify(x => x.GetStoppedAtAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetQueuedAtAsync_ReadsEachKeyOnce_UnreadableKeyIsUnknown()
        {
            SetupQueuedAt("msg:a", StoppedAt.AddSeconds(-1));
            _daprClient
                .Setup(x => x.GetStateAsync<HandOffQueueStopGuard.QueuedMessageStamp>(CommonConsts.CommonAiPubSubConsts.StateStoreName, "msg:b",
                    It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var queuedAt = await _guard.GetQueuedAtAsync(["msg:a", "msg:b", "msg:a"], TestContext.Current.CancellationToken);

            Assert.Equal(StoppedAt.AddSeconds(-1), queuedAt["msg:a"]);
            Assert.Null(queuedAt["msg:b"]);
            VerifyQueuedAtRead(Times.Exactly(2));
        }

        [Fact]
        public async Task DeleteQueuedMessageAsync_DeletesTheMsgState_AndSwallowsFailures()
        {
            _daprClient
                .Setup(x => x.DeleteStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            await _guard.DeleteQueuedMessageAsync(Key, TestContext.Current.CancellationToken);

            _daprClient.Verify(x => x.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<StateOptions>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
