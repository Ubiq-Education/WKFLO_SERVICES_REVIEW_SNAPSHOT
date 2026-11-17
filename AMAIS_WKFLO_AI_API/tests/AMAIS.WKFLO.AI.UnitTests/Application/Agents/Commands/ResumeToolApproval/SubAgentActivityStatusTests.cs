using AMAIS.WKFLO.AI.Application.Agents.Commands.InvokeAgent;
using AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>
    /// Bug 7722: the sub-agent activity updates of a hand-off follow its tool approval:
    /// started -> awaiting_approval -> resumed -> completed/failed (Allow), denied (Deny) or expired (no answer).
    /// </summary>
    public class SubAgentActivityStatusTests
    {
        private const int RootAgentId = 10;
        private const string Action = "image-edit";

        private readonly Mock<IToolApprovalStateService> _state = new();
        private readonly Mock<IRunnableAISyncService> _runnable = new();
        private readonly ChatContext _chatContext = new();
        private readonly Mock<IChatNotificationService> _notifications = new();
        private readonly List<string> _statuses = new();

        public SubAgentActivityStatusTests()
        {
            _runnable.Setup(x => x.CanResumeAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _notifications
                .Setup(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, int?, string, string, string, string, string, string>((_, _, _, status, _, _, _, _) => _statuses.Add(status))
                .Returns(Task.CompletedTask);
            // US 7959 / Task 7963: "failed" now carries a code (and never text).
            _notifications
                .Setup(x => x.SendSubagentActivityFailedUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => _statuses.Add(SubagentActivityStatus.Failed))
                .Returns(Task.CompletedTask);
        }

        private static ToolApprovalRecord SubAgentRecord(string status, string decision, int? rootAgentId = RootAgentId)
        {
            var record = ToolApprovalHistoryTests.Record(status, decision);
            record.RootAgentId = rootAgentId;
            record.ChatbotIconUrl = "https://example.test/icon.png";
            record.Chat = new ToolApprovalChatState { SubagentActivityAction = Action };
            return record;
        }

        private ResumeToolApprovalCommandHandler Handler(Mock<IToolApprovalNotifier> notifier = null) => new(_state.Object, _runnable.Object, _chatContext,
            NullLogger<ResumeToolApprovalCommandHandler>.Instance, (notifier ?? new Mock<IToolApprovalNotifier>()).Object, Mock.Of<IDateTimeProvider>(), _notifications.Object);

        private Task<Result> Resume(ResumeToolApprovalCommandHandler handler) =>
            handler.Handle(new ResumeToolApprovalCommand { ApprovalId = "approval-1", SessionId = "session-1" }, TestContext.Current.CancellationToken).AsTask();

        private void Returns(ToolApprovalRecord record, string answer, bool asksAgain = false)
        {
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _runnable.Setup(x => x.ResumeAfterToolApprovalAsync(record, It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    _chatContext.ToolApprovalRequestedThisTurn = asksAgain;
                    // What the approval filter does in the resumed run when it requests another approval.
                    if (asksAgain)
                        _notifications.Object.SendSubagentActivityUpdate(record.SessionId, record.AgentId, record.AgentName, SubagentActivityStatus.AwaitingApproval,
                            record.ChatbotIconUrl, Action);
                })
                .ReturnsAsync(answer);
        }

        // ---- the hand-off run ----------------------------------------------------------------------------

        [Fact]
        public async Task InvokeAgentCommandHandler_ReportsARunThatStoppedOnAnApproval()
        {
            var chatContext = new ChatContext();
            var runnable = new Mock<IRunnableAISyncService>();
            runnable.Setup(x => x.RunRunnableAgentByIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .Callback(() => chatContext.ToolApprovalRequestedThisTurn = true) // what the approval filter does in this scope
                .ReturnsAsync(new RunnableAgentData { Message = "Waiting for your approval." });

            var result = await new InvokeAgentCommandHandler(runnable.Object, Mock.Of<IDateTimeProvider>(), chatContext)
                .Handle(new InvokeAgentCommand { Id = 20, SessionId = "session-1", Input = new AgentInvocationInput { Text = "edit it" } }, TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<AgentInvocationResult>>(result).Data;
            Assert.True(data.Success);
            Assert.True(data.AwaitingApproval);
        }

        [Fact]
        public async Task InvokeAgentCommandHandler_ARunWithoutApprovalIsNotAwaiting()
        {
            var runnable = new Mock<IRunnableAISyncService>();
            runnable.Setup(x => x.RunRunnableAgentByIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunnableAgentData { Message = "Done." });

            var result = await new InvokeAgentCommandHandler(runnable.Object, Mock.Of<IDateTimeProvider>(), new ChatContext())
                .Handle(new InvokeAgentCommand { Id = 20, SessionId = "session-1", Input = new AgentInvocationInput { Text = "edit it" } }, TestContext.Current.CancellationToken);

            Assert.False(Assert.IsType<SuccessResult<AgentInvocationResult>>(result).Data.AwaitingApproval);
        }

        [Fact]
        public void ChatState_KeepsTheActivityActionAcrossTheApproval()
        {
            var state = ToolApprovalChatState.From(new ChatContext { SubagentActivityAction = Action }, rootIsWidget: false);

            Assert.Equal(Action, state.SubagentActivityAction);
            Assert.Equal(Action, state.ToChatContext("session-1").SubagentActivityAction);
        }

        // ---- resume after the decision -------------------------------------------------------------------

        [Fact]
        public async Task Allow_SendsResumedThenCompleted_WithTheSameFields()
        {
            Returns(SubAgentRecord("Approved", "AllowOnce"), "Edited.");

            await Resume(Handler());

            Assert.Equal([SubagentActivityStatus.Resumed, SubagentActivityStatus.Completed], _statuses);
            _notifications.Verify(x => x.SendSubagentActivityUpdate("session-1", 5, "Support Agent", SubagentActivityStatus.Completed,
                "https://example.test/icon.png", Action, null, null), Times.Once);
        }

        [Fact]
        public async Task Allow_RunProducesNoAnswer_SendsResumedThenFailed()
        {
            Returns(SubAgentRecord("Approved", "AllowOnce"), null);

            await Resume(Handler());

            Assert.Equal([SubagentActivityStatus.Resumed, SubagentActivityStatus.Failed], _statuses);
        }

        [Fact]
        public async Task Allow_RunThrows_SendsResumedThenFailed()
        {
            var record = SubAgentRecord("Approved", "AllowOnce");
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _runnable.Setup(x => x.ResumeAfterToolApprovalAsync(record, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => Resume(Handler()));

            Assert.Equal([SubagentActivityStatus.Resumed, SubagentActivityStatus.Failed], _statuses);
        }

        [Fact]
        public async Task Allow_ResumedRunAsksAgain_SendsResumedThenAwaitingApproval()
        {
            Returns(SubAgentRecord("Approved", "AllowOnce"), "Waiting for your approval.", asksAgain: true);

            await Resume(Handler());

            Assert.Equal([SubagentActivityStatus.Resumed, SubagentActivityStatus.AwaitingApproval], _statuses);
        }

        [Fact]
        public async Task Deny_SendsDeniedAfterTheResumedTurn()
        {
            var record = SubAgentRecord("Denied", "Deny");
            Returns(record, "Understood, I did not edit it.");
            _runnable.Setup(x => x.ResumeAfterToolApprovalAsync(record, It.IsAny<CancellationToken>()))
                .Callback(() => Assert.Empty(_statuses)) // nothing is sent before the resumed turn has run
                .ReturnsAsync("Understood, I did not edit it.");

            await Resume(Handler());

            Assert.Equal([SubagentActivityStatus.Denied], _statuses);
        }

        [Fact]
        public async Task Deny_ResumedTurnFails_SendsOnlyFailed()
        {
            Returns(SubAgentRecord("Denied", "Deny"), null);

            await Resume(Handler());

            Assert.Equal([SubagentActivityStatus.Failed], _statuses);
        }

        [Fact]
        public async Task Deny_ResumedTurnAsksForAnotherApproval_SendsOnlyAwaitingApproval()
        {
            Returns(SubAgentRecord("Denied", "Deny"), "I need your permission for something else.", asksAgain: true);

            await Resume(Handler());

            Assert.Equal([SubagentActivityStatus.AwaitingApproval], _statuses);
        }

        [Fact]
        public async Task Allow_ButSubAgentNoLongerAvailable_SendsFailed()
        {
            var record = SubAgentRecord("Approved", "AllowOnce");
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _runnable.Setup(x => x.CanResumeAsync(record, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await Resume(Handler());

            Assert.Equal([SubagentActivityStatus.Failed], _statuses);
        }

        [Theory]
        [InlineData("Approved", "AllowOnce")]
        [InlineData("Denied", "Deny")]
        public async Task RootAgentApproval_SendsNoSubAgentActivity(string status, string decision)
        {
            Returns(SubAgentRecord(status, decision, rootAgentId: null), "Done.");

            await Resume(Handler());

            Assert.Empty(_statuses);
        }

        // ---- expiry --------------------------------------------------------------------------------------

        [Fact]
        public async Task Expired_SubAgentApproval_SendsExpired()
        {
            var notifier = new ToolApprovalNotifier(_notifications.Object, Mock.Of<IChatStoreService>(), NullLogger<ToolApprovalNotifier>.Instance);

            await notifier.NotifyResolvedAsync(SubAgentRecord("Expired", "Expired"));

            Assert.Equal([SubagentActivityStatus.Expired], _statuses);
            _notifications.Verify(x => x.SendSubagentActivityUpdate("session-1", 5, "Support Agent", SubagentActivityStatus.Expired,
                "https://example.test/icon.png", Action, null, null), Times.Once);
        }

        [Theory]
        [InlineData("Expired", "Expired", null)]
        [InlineData("Denied", "Deny", RootAgentId)]
        [InlineData("Approved", "AllowOnce", RootAgentId)]
        public async Task NotifyResolved_OtherwiseSendsNoSubAgentActivity(string status, string decision, int? rootAgentId)
        {
            var notifier = new ToolApprovalNotifier(_notifications.Object, Mock.Of<IChatStoreService>(), NullLogger<ToolApprovalNotifier>.Instance);

            await notifier.NotifyResolvedAsync(SubAgentRecord(status, decision, rootAgentId));

            Assert.Empty(_statuses);
        }
    }
}
