using AMAIS.WKFLO.AI.UnitTests.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Models;
using Dapr;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatStop
{
    /// <summary>US 7501 (thread 97604): the first message of a session records its owner once, in the shared state store.</summary>
    public class ChatSessionOwnerStoreTests
    {
        private const string Key = "chat-session-owner-tenant-a:session-1";

        private readonly Mock<DaprClient> _dapr = new();
        private readonly RecordingLogger<ChatSessionOwnerStore> _logger = new();
        private readonly ChatSessionOwnerStore _store;

        public ChatSessionOwnerStoreTests()
        {
            _store = new ChatSessionOwnerStore(_dapr.Object, _logger);
        }

        private void SaveThrows(Exception exception) =>
            _dapr.Setup(x => x.TrySaveStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ChatSessionOwner>(), It.IsAny<string>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

        [Fact]
        public async Task Record_WritesInsertOnlyWithATtl_InOneRoundTrip()
        {
            await _store.RecordAsync("Tenant-A", "session-1", new ChatSessionOwner { AgentId = 5, UserId = "user-1" }, TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.TrySaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key,
                It.Is<ChatSessionOwner>(o => o.AgentId == 5 && o.UserId == "user-1"), "",
                It.Is<StateOptions>(o => o.Concurrency == ConcurrencyMode.FirstWrite),
                It.Is<IReadOnlyDictionary<string, string>>(m => m["ttlInSeconds"] == ChatSessionOwnerStore.StateTtlSeconds.ToString()),
                It.IsAny<CancellationToken>()), Times.Once);
            _dapr.Verify(x => x.GetStateAsync<ChatSessionOwner>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private void ReadReturns(ChatSessionOwner owner) =>
            _dapr.Setup(x => x.GetStateAsync<ChatSessionOwner>(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(owner);

        /// <summary>
        /// Every message after the first: the insert-only write finds the owner. Dapr's wording depends on the state store
        /// (Bug 8222: the Redis store's Lua-script rejection has no "etag" in it), so the owner is read back to recognise it.
        /// </summary>
        [Theory]
        [InlineData("failed saving state in state store statestore: possible etag mismatch. error from state store: ERR Error running script")]
        [InlineData("failed saving state in state store statestore-ai: failed to set key amais-wkflo-ai-webapi||chat-session-owner-tenant-a:session-1: "
            + "ERR user_script:14: failed to set key amais-wkflo-ai-webapi||chat-session-owner-tenant-a:session-1 script: d908b9553add63a82e03589b5c6d01a7654ef0f2, on @user_script:14.")]
        public async Task Record_OwnerAlreadyRecorded_IsNotAnErrorAndIsNotLogged(string stateStoreError)
        {
            SaveThrows(new DaprException("State operation failed: the Dapr endpoint indicated a failure.", new InvalidOperationException(stateStoreError)));
            ReadReturns(new ChatSessionOwner { AgentId = 5, UserId = "user-1" });

            var exception = await Record.ExceptionAsync(() =>
                _store.RecordAsync("tenant-a", "session-1", new ChatSessionOwner { AgentId = 7 }, TestContext.Current.CancellationToken));

            Assert.Null(exception);
            Assert.Empty(_logger.Entries);
            _dapr.Verify(x => x.GetStateAsync<ChatSessionOwner>(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, ConsistencyMode.Strong,
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>The write failed and no owner is recorded: a real failure, whatever the error says.</summary>
        [Fact]
        public async Task Record_StateStoreDown_LogsAWarningAndNeverFailsTheMessage()
        {
            SaveThrows(new DaprException("State operation failed: the Dapr endpoint indicated a failure.", new InvalidOperationException("connection refused")));
            ReadReturns(null);

            var exception = await Record.ExceptionAsync(() =>
                _store.RecordAsync("tenant-a", "session-1", new ChatSessionOwner { AgentId = 5 }, TestContext.Current.CancellationToken));

            Assert.Null(exception);
            Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
        }

        [Fact]
        public async Task Record_StateStoreDown_AndTheReadBackFails_LogsOneWarningAndNeverFailsTheMessage()
        {
            SaveThrows(new DaprException("State operation failed: the Dapr endpoint indicated a failure.", new InvalidOperationException("connection refused")));
            _dapr.Setup(x => x.GetStateAsync<ChatSessionOwner>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ConsistencyMode?>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DaprException("State read failed: the read-back error"));

            var exception = await Record.ExceptionAsync(() =>
                _store.RecordAsync("tenant-a", "session-1", new ChatSessionOwner { AgentId = 5 }, TestContext.Current.CancellationToken));

            Assert.Null(exception);
            var entry = Assert.Single(_logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("the read-back error", (string)entry.Values["ReadError"]);
        }
    }
}
