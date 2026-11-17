using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.AI.UnitTests.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.StopChatSession
{
    /// <summary>US 7501: the Stop command - record, broadcast, cancel local turns, tell the chat, expire approvals, drop the replay.</summary>
    public class StopChatSessionCommandHandlerTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<IChatStopStateStore> _stateStore = new();
        private readonly Mock<IChatStopBroadcaster> _broadcaster = new();
        private readonly ChatTurnRegistry _registry = new();
        private readonly Mock<IToolApprovalStateService> _approvals = new();
        private readonly Mock<IContactFormReplayStateService> _contactFormReplay = new();
        private readonly Mock<IChatNotificationService> _notifications = new();
        private readonly RecordingLogger<StopChatSessionProcessor> _logger = new();
        private readonly Mock<IHandOffQueueClearScheduler> _handOffQueues = new();
        private readonly StopChatSessionProcessor _processor;

        public StopChatSessionCommandHandlerTests()
        {
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            _approvals.Setup(x => x.ExpireOpenForSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ToolApprovalRecord>());

            var gate = new ToolApprovalSessionGate(_approvals.Object, Mock.Of<IToolApprovalNotifier>(), dateTimeProvider, NullLogger<ToolApprovalSessionGate>.Instance);
            _processor = new StopChatSessionProcessor(_stateStore.Object, _broadcaster.Object, _registry, gate, _contactFormReplay.Object,
                _notifications.Object, dateTimeProvider, _logger, new TenantContext { TenantName = "tenant-a" }, _handOffQueues.Object);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Handle_StopsTheSession(bool tenantChat)
        {
            using var runningTurn = _registry.Register("tenant-a", "session-1", Now.AddSeconds(-10));
            using var nextTurn = _registry.Register("tenant-a", "session-1", Now.AddSeconds(1));

            // Act
            var result = tenantChat
                ? await new StopTenantChatSessionCommandHandler(_processor).Handle(new StopTenantChatSessionCommand { Id = 5, SessionId = "session-1" }, TestContext.Current.CancellationToken)
                : await new StopPublicChatSessionCommandHandler(_processor).Handle(new StopPublicChatSessionCommand { ShareId = "share-1", SessionId = "session-1" }, TestContext.Current.CancellationToken);

            // Assert
            var data = Assert.IsType<SuccessResult<StopChatSessionResult>>(result).Data;
            Assert.Equal("session-1", data.SessionId);
            Assert.Equal(Now, data.StoppedAt);

            _stateStore.Verify(x => x.SetStoppedAsync("tenant-a", "session-1", Now, It.IsAny<CancellationToken>()), Times.Once);
            _broadcaster.Verify(x => x.PublishAsync(new ChatStopSignal("tenant-a", "session-1", Now), It.IsAny<CancellationToken>()), Times.Once);

            Assert.True(runningTurn.StopRequested);
            Assert.False(nextTurn.StopRequested);

            _notifications.Verify(x => x.SendTypingIndicator("session-1", false, null, null, null), Times.Once);
            _notifications.Verify(x => x.SendConversationStateUpdate("session-1", "idle", null), Times.Once);
            _notifications.Verify(x => x.SendConversationStoppedAsync("session-1", Now), Times.Once);

            _approvals.Verify(x => x.ExpireOpenForSessionAsync("tenant-a", "session-1", Now, It.IsAny<CancellationToken>()), Times.Once);
            _contactFormReplay.Verify(x => x.ConsumePendingReplayAsync("session-1", It.IsAny<CancellationToken>()), Times.Once);

            // Approved change 3: the queued hand-offs of the session's agents are dropped.
            if (tenantChat)
                _handOffQueues.Verify(x => x.Schedule("session-1", 5, null, Now), Times.Once);
            else
                _handOffQueues.Verify(x => x.Schedule("session-1", null, "share-1", Now), Times.Once);
        }

        [Theory]
        [InlineData("broadcast")]
        [InlineData("replay")]
        [InlineData("hand-off queues")]
        public async Task Handle_BestEffortStepFails_StillStopsAndTellsTheChat(string failingStep)
        {
            if (failingStep == "broadcast")
                _broadcaster.Setup(x => x.PublishAsync(It.IsAny<ChatStopSignal>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));
            else if (failingStep == "hand-off queues")
                _handOffQueues.Setup(x => x.Schedule(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
                    .Throws(new InvalidOperationException("thread pool exhausted"));
            else
                _contactFormReplay.Setup(x => x.ConsumePendingReplayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("state store down"));

            using var runningTurn = _registry.Register("tenant-a", "session-1", Now.AddSeconds(-10));

            // Act
            var result = await _processor.StopAsync("session-1", 5, null);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            Assert.True(runningTurn.StopRequested);
            _notifications.Verify(x => x.SendConversationStoppedAsync("session-1", Now), Times.Once);
        }

        [Fact]
        public async Task Handle_TellsTheChatBeforeClearingTheHandOffQueues()
        {
            // The clear only starts once the chat was told (the ~1s target), and is never awaited (see the scheduler).
            var order = new List<string>();
            _notifications.Setup(x => x.SendConversationStoppedAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
                .Callback(() => order.Add("stopped")).Returns(Task.CompletedTask);
            _handOffQueues.Setup(x => x.Schedule(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
                .Callback(() => order.Add("clear"));

            await _processor.StopAsync("session-1", 5, null);

            Assert.Equal(["stopped", "clear"], order);
        }

        [Fact]
        public async Task ExpireOpenApprovals_NotifyFails_StillReportsEveryExpiredApproval()
        {
            var notifier = new Mock<IToolApprovalNotifier>();
            notifier.Setup(x => x.NotifyResolvedAsync(It.IsAny<ToolApprovalRecord>())).ThrowsAsync(new InvalidOperationException("hub down"));
            _approvals.Setup(x => x.ExpireOpenForSessionAsync("tenant-a", "session-1", Now, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ToolApprovalRecord> { new() { ApprovalId = "a1" }, new() { ApprovalId = "a2" } });
            var gate = new ToolApprovalSessionGate(_approvals.Object, notifier.Object, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now),
                NullLogger<ToolApprovalSessionGate>.Instance);

            var expired = await gate.ExpireOpenApprovalsAsync("tenant-a", "session-1", TestContext.Current.CancellationToken);

            Assert.Equal(2, expired);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task StopAsync_SummaryReportsWhetherTheBroadcastWasPublished(bool published)
        {
            _broadcaster.Setup(x => x.PublishAsync(It.IsAny<ChatStopSignal>(), It.IsAny<CancellationToken>())).ReturnsAsync(published);

            await _processor.StopAsync("session-1", 5, null);

            Assert.Equal(published, Assert.Single(_logger.With("BroadcastPublished")).Values["BroadcastPublished"]);
        }

        [Fact]
        public async Task StopAsync_LogsASummaryOfWhatTheStopDid()
        {
            _broadcaster.Setup(x => x.PublishAsync(It.IsAny<ChatStopSignal>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));
            _approvals.Setup(x => x.ExpireOpenForSessionAsync("tenant-a", "session-1", Now, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ToolApprovalRecord> { new() { ApprovalId = "a1" }, new() { ApprovalId = "a2" } });
            using var runningTurn = _registry.Register("tenant-a", "session-1", Now.AddSeconds(-10));

            await _processor.StopAsync("session-1", 5, null);

            var summary = Assert.Single(_logger.With("LocalTurnsCancelled")).Values;
            Assert.Equal(1, summary["LocalTurnsCancelled"]);
            Assert.Equal(true, summary["StopStateRecorded"]);
            Assert.Equal(false, summary["BroadcastPublished"]);
            Assert.Equal(2, summary["ApprovalsExpired"]);
            Assert.Equal(true, summary["ReplayCleared"]);
            Assert.Equal(true, summary["HandOffClearScheduled"]);
        }
    }
}
