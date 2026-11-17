using AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Workflows;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>WI 7575: background resume after a decision, and the 10-minute expiry activity.</summary>
    public class ResumeToolApprovalCommandHandlerTests
    {
        private readonly Mock<IToolApprovalStateService> _state = new();
        private readonly Mock<IRunnableAISyncService> _runnable = new();
        private readonly ChatContext _chatContext = new();
        private readonly Mock<IToolApprovalNotifier> _notifier = new();

        public ResumeToolApprovalCommandHandlerTests()
        {
            _runnable.Setup(x => x.CanResumeAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }

        private ResumeToolApprovalCommandHandler Handler() => new(_state.Object, _runnable.Object, _chatContext, NullLogger<ResumeToolApprovalCommandHandler>.Instance,
            _notifier.Object, Mock.Of<IDateTimeProvider>(), Mock.Of<AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer.IChatNotificationService>());

        [Fact]
        public async Task Approved_ButAgentNoLongerAvailable_RefusesWithoutRunning()
        {
            var record = ToolApprovalHistoryTests.Record("Approved", "AllowOnce");
            var refused = ToolApprovalHistoryTests.Record("Denied", "Deny");
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _state.Setup(x => x.MarkRefusedAsync("approval-1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(refused);
            _runnable.Setup(x => x.CanResumeAsync(record, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await Handler().Handle(new ResumeToolApprovalCommand { ApprovalId = "approval-1", SessionId = "session-1" }, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.NotFound, result.ResultType);
            _runnable.Verify(x => x.ResumeAfterToolApprovalAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
            _notifier.Verify(x => x.NotifyResolvedAsync(refused), Times.Once);
            _notifier.Verify(x => x.PersistNotRunAsync(refused, ToolApprovalHistory.NoLongerAvailableResult), Times.Once);
        }

        [Fact]
        public async Task Denied_DoesNotNeedTheAvailabilityCheck()
        {
            var record = ToolApprovalHistoryTests.Record("Denied", "Deny");
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _runnable.Setup(x => x.CanResumeAsync(record, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            _runnable.Setup(x => x.ResumeAfterToolApprovalAsync(record, It.IsAny<CancellationToken>())).ReturnsAsync("Understood.");

            var result = await Handler().Handle(new ResumeToolApprovalCommand { ApprovalId = "approval-1", SessionId = "session-1" }, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Ok, result.ResultType);
        }

        [Theory]
        [InlineData("Approved", "AllowOnce")]
        [InlineData("Denied", "Deny")]
        public async Task Decided_ResumesWithApprovalsAvailable(string status, string decision)
        {
            var record = ToolApprovalHistoryTests.Record(status, decision);
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _runnable.Setup(x => x.ResumeAfterToolApprovalAsync(record, It.IsAny<CancellationToken>())).ReturnsAsync("Done.");

            var result = await Handler().Handle(new ResumeToolApprovalCommand { ApprovalId = "approval-1", SessionId = "session-1" }, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Ok, result.ResultType);
            Assert.True(_chatContext.ToolApprovalsAvailable);
            Assert.Equal("session-1", _chatContext.SessionId);
        }

        [Theory]
        [InlineData("Pending", null, "session-1")]
        [InlineData("Expired", "Expired", "session-1")]
        [InlineData("Approved", "AllowOnce", "other-session")]
        public async Task PendingExpiredOrForeign_IsNotResumed(string status, string decision, string sessionId)
        {
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(ToolApprovalHistoryTests.Record(status, decision));

            var result = await Handler().Handle(new ResumeToolApprovalCommand { ApprovalId = "approval-1", SessionId = sessionId }, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.NotFound, result.ResultType);
            _runnable.Verify(x => x.ResumeAfterToolApprovalAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExpiryActivity_ExpiresPendingApproval_NotifiesAndPersists()
        {
            var now = new DateTimeOffset(2026, 5, 1, 10, 10, 0, TimeSpan.Zero);
            var expired = ToolApprovalHistoryTests.Record("Expired", "Expired");
            _state.Setup(x => x.TryExpireAsync("approval-1", now, It.IsAny<CancellationToken>())).ReturnsAsync(expired);
            var notifier = new Mock<IToolApprovalNotifier>();
            var activity = new ToolApprovalExpiryWorkflowActivity(_state.Object, notifier.Object, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == now),
                NullLogger<ToolApprovalExpiryWorkflowActivity>.Instance);

            Assert.True(await activity.RunAsync(null, new ToolApprovalExpiryWorkflowInput { ApprovalId = "approval-1" }));

            notifier.Verify(x => x.NotifyResolvedAsync(expired), Times.Once);
            notifier.Verify(x => x.PersistExpiredAsync(expired), Times.Once);
        }

        [Fact]
        public async Task ExpiryActivity_AlreadyDecided_DoesNothing()
        {
            var notifier = new Mock<IToolApprovalNotifier>();
            var activity = new ToolApprovalExpiryWorkflowActivity(_state.Object, notifier.Object, Mock.Of<IDateTimeProvider>(), NullLogger<ToolApprovalExpiryWorkflowActivity>.Instance);

            Assert.True(await activity.RunAsync(null, new ToolApprovalExpiryWorkflowInput { ApprovalId = "approval-1" }));

            notifier.VerifyNoOtherCalls();
        }
    }
}
