using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using Dapr.Client;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval
{
    /// <summary>WI 7575: single-use decisions and expiry on top of the Dapr state store (ETag concurrency).</summary>
    public class ToolApprovalStateServiceTests
    {
        private const string Store = "statestore-ai";
        private const string Key = "tool-approval-approval-1";
        private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 5, 0, TimeSpan.Zero);

        private readonly Mock<DaprClient> _dapr = new();
        private readonly ToolApprovalStateService _service;

        public ToolApprovalStateServiceTests()
        {
            _service = new ToolApprovalStateService(_dapr.Object);
            // Open-index updates succeed (not the subject of these tests).
            _dapr.Setup(x => x.GetStateAndETagAsync<List<string>>(Store, It.Is<string>(k => k.StartsWith("tool-approval-open-")), It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new List<string> { "approval-1" }, "etag-open"));
            _dapr.Setup(x => x.TrySaveStateAsync(Store, It.Is<string>(k => k.StartsWith("tool-approval-open-")), It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        private void SetupRecord(ToolApprovalRecord record, string etag = "etag-1") =>
            _dapr.Setup(x => x.GetStateAndETagAsync<ToolApprovalRecord>(Store, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((record, etag));

        private void SetupSave(params bool[] results)
        {
            var queue = new Queue<bool>(results);
            _dapr.Setup(x => x.TrySaveStateAsync(Store, Key, It.IsAny<ToolApprovalRecord>(), It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => queue.Count > 0 ? queue.Dequeue() : true);
        }

        /// <summary>US 7501 (review fix): a Stop only expires the approvals of its own tenant.</summary>
        [Theory]
        [InlineData("Tenant-A", 1)]
        [InlineData("tenant-b", 0)]
        public async Task ExpireOpenForSession_ForATenant_OnlyExpiresThatTenantsApprovals(string recordTenant, int expectedExpired)
        {
            _dapr.Setup(x => x.GetStateAsync<List<string>>(Store, It.Is<string>(k => k.StartsWith("tool-approval-open-")), It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "approval-1" });
            var record = ToolApprovalHistoryTests.Record();
            record.TenantName = recordTenant;
            _dapr.Setup(x => x.GetStateAsync<ToolApprovalRecord>(Store, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
            SetupRecord(record);
            SetupSave(true);

            var expired = await _service.ExpireOpenForSessionAsync("tenant-a", "session-1", Now, TestContext.Current.CancellationToken);

            Assert.Equal(expectedExpired, expired.Count);
            _dapr.Verify(x => x.TrySaveStateAsync(Store, Key, It.IsAny<ToolApprovalRecord>(), It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                expectedExpired == 1 ? Times.Once() : Times.Never());
        }

        [Theory]
        [InlineData("AllowOnce", "Approved")]
        [InlineData("AllowForConversation", "Approved")]
        [InlineData("Deny", "Denied")]
        public async Task TryDecide_Pending_RecordsDecisionWithEtag(string decision, string status)
        {
            SetupRecord(ToolApprovalHistoryTests.Record());
            SetupSave(true);

            var (outcome, record) = await _service.TryDecideAsync("approval-1", decision, Now, TestContext.Current.CancellationToken);

            Assert.Equal(ToolApprovalDecisionOutcome.Decided, outcome);
            Assert.Equal(decision, record.Decision);
            Assert.Equal(status, record.Status);
            Assert.Equal(Now, record.DecidedAt);
            _dapr.Verify(x => x.TrySaveStateAsync(Store, Key, It.IsAny<ToolApprovalRecord>(), "etag-1", It.IsAny<StateOptions>(),
                It.Is<IReadOnlyDictionary<string, string>>(m => m["ttlInSeconds"] == "86400"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task TryDecide_AlreadyDecided_ReturnsAlreadyDecided_WithoutWriting()
        {
            SetupRecord(ToolApprovalHistoryTests.Record("Denied", "Deny"));

            var (outcome, _) = await _service.TryDecideAsync("approval-1", "AllowOnce", Now, TestContext.Current.CancellationToken);

            Assert.Equal(ToolApprovalDecisionOutcome.AlreadyDecided, outcome);
            _dapr.Verify(x => x.TrySaveStateAsync(Store, Key, It.IsAny<ToolApprovalRecord>(), It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task TryDecide_PastExpiry_MarksExpired()
        {
            SetupRecord(ToolApprovalHistoryTests.Record());
            SetupSave(true);

            var (outcome, record) = await _service.TryDecideAsync("approval-1", "AllowOnce", Now.AddMinutes(30), TestContext.Current.CancellationToken);

            Assert.Equal(ToolApprovalDecisionOutcome.ExpiredNow, outcome);
            Assert.Equal("Expired", record.Status);
            Assert.Equal("Expired", record.Decision);
        }

        [Fact]
        public async Task TryDecide_AlreadyExpired_ReturnsExpired()
        {
            SetupRecord(ToolApprovalHistoryTests.Record("Expired", "Expired"));

            var (outcome, _) = await _service.TryDecideAsync("approval-1", "AllowOnce", Now, TestContext.Current.CancellationToken);

            Assert.Equal(ToolApprovalDecisionOutcome.Expired, outcome);
        }

        [Fact]
        public async Task TryDecide_ConcurrentWriteWins_RetriesAndSeesItDecided()
        {
            // First read: pending; the ETag write loses; second read: the other instance's decision.
            var reads = new Queue<(ToolApprovalRecord, string)>(new[]
            {
                (ToolApprovalHistoryTests.Record(), "etag-1"),
                (ToolApprovalHistoryTests.Record("Approved", "AllowOnce"), "etag-2")
            });
            _dapr.Setup(x => x.GetStateAndETagAsync<ToolApprovalRecord>(Store, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => reads.Dequeue());
            SetupSave(false);

            var (outcome, _) = await _service.TryDecideAsync("approval-1", "Deny", Now, TestContext.Current.CancellationToken);

            Assert.Equal(ToolApprovalDecisionOutcome.AlreadyDecided, outcome);
        }

        [Fact]
        public async Task TryDecide_Unknown_ReturnsNotFound()
        {
            SetupRecord(null, null);

            var (outcome, record) = await _service.TryDecideAsync("approval-1", "AllowOnce", Now, TestContext.Current.CancellationToken);

            Assert.Equal(ToolApprovalDecisionOutcome.NotFound, outcome);
            Assert.Null(record);
        }

        [Fact]
        public async Task TryExpire_OnlyExpiresPending()
        {
            SetupRecord(ToolApprovalHistoryTests.Record());
            SetupSave(true);
            var expired = await _service.TryExpireAsync("approval-1", Now, TestContext.Current.CancellationToken);
            Assert.Equal("Expired", expired.Status);

            SetupRecord(ToolApprovalHistoryTests.Record("Approved", "AllowOnce"));
            Assert.Null(await _service.TryExpireAsync("approval-1", Now, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ConversationGrants_AddThenCheck()
        {
            const string grantsKey = "tool-approval-grants-session-1";
            _dapr.Setup(x => x.GetStateAndETagAsync<List<string>>(Store, grantsKey, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(((List<string>)null, (string)null));
            _dapr.Setup(x => x.GetStateAsync<List<string>>(Store, grantsKey, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "7:search" });

            await _service.AddConversationGrantAsync("session-1", "7:search", TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.SaveStateAsync(Store, grantsKey, It.Is<List<string>>(l => l.Contains("7:search")), It.IsAny<StateOptions>(),
                It.Is<IReadOnlyDictionary<string, string>>(m => m["ttlInSeconds"] == "604800"), It.IsAny<CancellationToken>()), Times.Once);
            Assert.True(await _service.HasConversationGrantAsync("session-1", "7:search", TestContext.Current.CancellationToken));
            Assert.False(await _service.HasConversationGrantAsync("session-1", "7:delete", TestContext.Current.CancellationToken));
            Assert.False(await _service.HasConversationGrantAsync(null, "7:search", TestContext.Current.CancellationToken));
        }
    }
}
