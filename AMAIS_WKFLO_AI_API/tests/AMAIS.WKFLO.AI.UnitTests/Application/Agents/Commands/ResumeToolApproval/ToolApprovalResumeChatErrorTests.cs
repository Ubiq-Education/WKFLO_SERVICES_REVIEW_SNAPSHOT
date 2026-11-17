using AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval;
using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>
    /// US 7959 / Task 7963: a resume turn after a tool approval decision that fails reaches the chat once, with a code: a
    /// terminal error for the chat's own agent, a non-terminal per-agent failure (then idle) for a hand-off sub-agent.
    /// </summary>
    public class ToolApprovalResumeChatErrorTests
    {
        private const int RootAgentId = 10;

        private readonly ChatErrorHarness _harness = new();
        private readonly Mock<IToolApprovalStateService> _state = new();
        private readonly Mock<IRunnableAISyncService> _runnable = new();
        private readonly ChatTurnFailureState _failureState = new();

        public ToolApprovalResumeChatErrorTests()
        {
            _runnable.Setup(x => x.CanResumeAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }

        private static ToolApprovalRecord Record(int? rootAgentId)
        {
            var record = ToolApprovalHistoryTests.Record(ToolApprovalConsts.Status.Approved, ToolApprovalConsts.Decision.AllowOnce);
            record.RootAgentId = rootAgentId;
            record.Chat = new ToolApprovalChatState { SubagentActivityAction = "image-edit" };
            return record;
        }

        private ResumeToolApprovalCommandHandler Handler() => new(_state.Object, _runnable.Object, new ChatContext(),
            NullLogger<ResumeToolApprovalCommandHandler>.Instance, new Mock<IToolApprovalNotifier>().Object,
            Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == ChatErrorHarness.Now.AddSeconds(-10)), _harness.Chat.Object,
            _harness.Reporter, _failureState);

        private async Task Resume(ToolApprovalRecord record, Func<Task<string>> run)
        {
            _state.Setup(x => x.GetAsync(record.ApprovalId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _runnable.Setup(x => x.ResumeAfterToolApprovalAsync(record, It.IsAny<CancellationToken>())).Returns(run);

            try
            {
                await Handler().Handle(new ResumeToolApprovalCommand { ApprovalId = record.ApprovalId, SessionId = record.SessionId }, TestContext.Current.CancellationToken);
            }
            catch (InvalidOperationException)
            {
                // A thrown resume is rethrown for the queue's log; what matters is what the chat received.
            }
        }

        [Fact]
        public async Task ChatAgentResumeFails_EndsTheTurnWithOneTerminalError()
        {
            var record = Record(rootAgentId: null);

            await Resume(record, () =>
            {
                _failureState.Record(ChatErrorCodes.ChatModelRateLimited, record.AgentId);
                return Task.FromResult<string>(null);
            });

            var marker = Assert.Single(_harness.Chat.Markers());
            Assert.Equal(true, marker.Message.Metadata[CommonMetadataConsts.IsTerminal]);
            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, marker.Message.Metadata[CommonMetadataConsts.ErrorCode]);
            Assert.Equal([false], _harness.Chat.TypingOf(record.AgentId));
            Assert.Equal(["error"], _harness.Chat.States());
            Assert.Empty(_harness.Chat.SubagentUpdatesOf(record.AgentId));
        }

        [Fact]
        public async Task ChatAgentResumeFailsAfterItsAnswerWasDelivered_ReportsNoError_StopsTheBubbleAndGoesIdle()
        {
            // PR 16990 review / Task 7961 rule: e.g. saving the history threw after the answer reached the chat.
            var record = Record(rootAgentId: null);

            await Resume(record, () =>
            {
                _failureState.MarkAnswerDelivered();
                throw new InvalidOperationException("SENTINEL history save failed");
            });

            Assert.Empty(_harness.Chat.Markers());
            Assert.DoesNotContain("error", _harness.Chat.States());
            Assert.Equal(["idle"], _harness.Chat.States());
            Assert.Equal([false], _harness.Chat.TypingOf(record.AgentId));
            _harness.Chat.AssertNoLeak("SENTINEL");
        }

        [Fact]
        public async Task ChatAgentResumeRecordsAFailureAfterItsAnswerWasDelivered_ReportsNoError()
        {
            var record = Record(rootAgentId: null);

            await Resume(record, () =>
            {
                _failureState.MarkAnswerDelivered();
                _failureState.Record(ChatErrorCodes.ChatInfrastructureError, record.AgentId);
                return Task.FromResult("The answer.");
            });

            Assert.Empty(_harness.Chat.Markers());
            Assert.Equal(["idle"], _harness.Chat.States());
        }

        [Fact]
        public async Task QueueReportsAFailureAfterTheAnswerWasDelivered_ReportsNoError()
        {
            var record = Record(rootAgentId: null);

            await ToolApprovalResumeFailure.ReportAsync(_harness.Reporter, _harness.Chat.Object, record, ChatErrorCodes.ChatTurnFailed, null,
                NullLogger.Instance, answerDelivered: true);

            Assert.Empty(_harness.Chat.Markers());
            Assert.Equal(["idle"], _harness.Chat.States());
        }

        [Fact]
        public async Task SubAgentResumeThrows_FailsOnlyThatAgent_ThenIdle_WithACodeAndNoText()
        {
            var record = Record(RootAgentId);

            await Resume(record, () => throw new InvalidOperationException("SENTINEL boom at Foo.Bar()"));

            var marker = Assert.Single(_harness.Chat.Markers());
            Assert.Equal(false, marker.Message.Metadata[CommonMetadataConsts.IsTerminal]);
            Assert.Equal(["idle"], _harness.Chat.States());
            var failed = Assert.Single(_harness.Chat.SubagentUpdatesOf(record.AgentId), u => u.Status == SubagentActivityStatus.Failed);
            Assert.Equal(ChatErrorCodes.ChatTurnFailed, failed.ErrorCode);
            Assert.Null(failed.Error);
            Assert.Equal("image-edit", failed.Action);
            _harness.Chat.AssertNoLeak("SENTINEL", "Foo.Bar", "InvalidOperationException");
        }

        [Fact]
        public async Task SubAgentResumeProducesNoAnswer_IsReportedAsUnavailable_NotWithText()
        {
            var record = Record(RootAgentId);

            await Resume(record, () => Task.FromResult<string>(null));

            var failed = Assert.Single(_harness.Chat.SubagentUpdatesOf(record.AgentId), u => u.Status == SubagentActivityStatus.Failed);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, failed.ErrorCode);
            _harness.Chat.AssertNoLeak("produced no answer");
        }

        [Fact]
        public async Task ResumeStreamFailedButReturned_IsStillAFailure()
        {
            var record = Record(rootAgentId: null);

            await Resume(record, () =>
            {
                _failureState.Record(ChatErrorCodes.ChatModelUnavailable, record.AgentId);
                return Task.FromResult("partial answ");
            });

            Assert.Equal(ChatErrorCodes.ChatModelUnavailable, _harness.Chat.Markers().Single().Message.Metadata[CommonMetadataConsts.ErrorCode]);
        }

        [Fact]
        public async Task StoppedAfterTheResumeStarted_ReportsNothing()
        {
            var record = Record(RootAgentId);
            _harness.StoppedAt(ChatErrorHarness.Now);

            await Resume(record, () => Task.FromResult<string>(null));

            Assert.Empty(_harness.Chat.Markers());
            Assert.DoesNotContain(_harness.Chat.SubagentUpdatesOf(record.AgentId), u => u.Status == SubagentActivityStatus.Failed);
        }

        [Fact]
        public async Task ReportedByTheHandlerAndAgainByTheQueue_ReachesTheChatOnce()
        {
            var record = Record(RootAgentId);

            await ToolApprovalResumeFailure.ReportAsync(_harness.Reporter, _harness.Chat.Object, record, ChatErrorCodes.ChatModelTimeout, null, NullLogger.Instance);
            await ToolApprovalResumeFailure.ReportAsync(_harness.Reporter, _harness.Chat.Object, record, ChatErrorCodes.ChatTurnFailed, null, NullLogger.Instance);

            Assert.Single(_harness.Chat.Markers());
            Assert.Single(_harness.Chat.SubagentUpdatesOf(record.AgentId));
        }
    }
}
