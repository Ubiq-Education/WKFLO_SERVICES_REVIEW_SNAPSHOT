using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.DecideToolApproval
{
    /// <summary>WI 7575: decision endpoints - ownership checks, single use (409), expiry (410), grant and resume.</summary>
    public class ToolApprovalDecisionProcessorTests
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 5, 0, TimeSpan.Zero);

        private readonly Mock<IToolApprovalStateService> _state = new();
        private readonly Mock<IToolApprovalNotifier> _notifier = new();
        private readonly Mock<IToolApprovalResumeQueue> _queue = new();
        private readonly UserContext _userContext = new();
        private readonly ToolApprovalDecisionProcessor _processor;

        public ToolApprovalDecisionProcessorTests()
        {
            _processor = new ToolApprovalDecisionProcessor(_state.Object, _notifier.Object, _queue.Object,
                new TenantContext { TenantName = "TestTenant" }, _userContext, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now));
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(ToolApprovalHistoryTests.Record());
        }

        private static DecideTenantToolApprovalCommand Command(string decision = "AllowOnce", string sessionId = "session-1") =>
            new() { Id = 5, ApprovalId = "approval-1", SessionId = sessionId, Decision = decision };

        private void Decides(ToolApprovalDecisionOutcome outcome, ToolApprovalRecord record) =>
            _state.Setup(x => x.TryDecideAsync("approval-1", It.IsAny<string>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync((outcome, record));

        [Theory]
        [InlineData("AllowOnce", "Approved", false)]
        [InlineData("AllowForConversation", "Approved", true)]
        [InlineData("Deny", "Denied", false)]
        public async Task Decided_NotifiesAndQueuesResume_GrantOnlyForConversation(string decision, string status, bool expectGrant)
        {
            Decides(ToolApprovalDecisionOutcome.Decided, ToolApprovalHistoryTests.Record(status, decision));

            var result = await _processor.DecideAsync(Command(decision), 5, TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<ToolApprovalDecisionResult>>(result).Data;
            Assert.Equal(decision, data.Decision);
            Assert.Equal(status, data.Status);
            _notifier.Verify(x => x.NotifyResolvedAsync(It.Is<ToolApprovalRecord>(r => r.Decision == decision)), Times.Once);
            _queue.Verify(x => x.EnqueueAsync(It.Is<ResumeToolApprovalCommand>(c => c.ApprovalId == "approval-1" && c.SessionId == "session-1"), It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Once);
            _state.Verify(x => x.AddConversationGrantAsync("session-1", "7:delete_document", It.IsAny<CancellationToken>()), expectGrant ? Times.Once() : Times.Never());
        }

        [Fact]
        public async Task AlreadyDecided_Returns409()
        {
            Decides(ToolApprovalDecisionOutcome.AlreadyDecided, ToolApprovalHistoryTests.Record("Approved", "AllowOnce"));

            var result = await _processor.DecideAsync(Command("Deny"), 5, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Conflict, result.ResultType);
            Assert.Equal(ErrorConst.ToolApprovalAlreadyDecided, Assert.Single(((ConflictResult)result).Errors).Code);
            _queue.Verify(x => x.EnqueueAsync(It.IsAny<ResumeToolApprovalCommand>(), It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExpiredByThisCall_Returns410_AndPersistsTheExpiry()
        {
            Decides(ToolApprovalDecisionOutcome.ExpiredNow, ToolApprovalHistoryTests.Record("Expired", "Expired"));

            var result = await _processor.DecideAsync(Command(), 5, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Gone, result.ResultType);
            _notifier.Verify(x => x.NotifyResolvedAsync(It.IsAny<ToolApprovalRecord>()), Times.Once);
            _notifier.Verify(x => x.PersistExpiredAsync(It.IsAny<ToolApprovalRecord>()), Times.Once);
            _queue.Verify(x => x.EnqueueAsync(It.IsAny<ResumeToolApprovalCommand>(), It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AlreadyExpired_LateClick_Returns410_WithoutNotifyingOrPersistingAgain()
        {
            Decides(ToolApprovalDecisionOutcome.Expired, ToolApprovalHistoryTests.Record("Expired", "Expired"));

            var result = await _processor.DecideAsync(Command(), 5, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Gone, result.ResultType);
            _notifier.VerifyNoOtherCalls();
            _queue.Verify(x => x.EnqueueAsync(It.IsAny<ResumeToolApprovalCommand>(), It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Decided_PassesTheDecidedRecordToTheResume()
        {
            var decided = ToolApprovalHistoryTests.Record("Approved", "AllowOnce");
            Decides(ToolApprovalDecisionOutcome.Decided, decided);

            await _processor.DecideAsync(Command(), 5, TestContext.Current.CancellationToken);

            _queue.Verify(x => x.EnqueueAsync(It.IsAny<ResumeToolApprovalCommand>(), decided, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(null, null, true)]
        [InlineData(null, "00000000-0000-0000-0000-000000000000", true)]
        [InlineData("00000000-0000-0000-0000-000000000000", "", true)]
        [InlineData("user-a", "USER-A", true)]
        [InlineData("user-a", "user-b", false)]
        [InlineData("user-a", null, false)]
        [InlineData(null, "user-b", false)]
        public async Task OnlyTheSameIdentityMayDecide(string recordUser, string callerUser, bool allowed)
        {
            var record = ToolApprovalHistoryTests.Record();
            record.UserId = recordUser;
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _userContext.Guid = callerUser;
            Decides(ToolApprovalDecisionOutcome.Decided, ToolApprovalHistoryTests.Record("Approved", "AllowOnce"));

            var result = await _processor.DecideAsync(Command(), 5, TestContext.Current.CancellationToken);

            Assert.Equal(allowed ? ResultType.Ok : ResultType.NotFound, result.ResultType);
        }

        [Theory]
        [InlineData("Approve")]
        [InlineData("Expired")]
        [InlineData(null)]
        public async Task InvalidDecision_Returns400(string decision)
        {
            var result = await _processor.DecideAsync(Command(decision), 5, TestContext.Current.CancellationToken);

            Assert.Equal(ErrorConst.ToolApprovalDecisionInvalid, Assert.Single(Assert.IsType<InvalidResult>(result).Errors).Code);
        }

        [Fact]
        public async Task OtherSession_Returns404()
        {
            var result = await _processor.DecideAsync(Command(sessionId: "another-session"), 5, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.NotFound, result.ResultType);
            _state.Verify(x => x.TryDecideAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task OtherAgent_Returns404()
        {
            var result = await _processor.DecideAsync(Command(), 99, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.NotFound, result.ResultType);
        }

        [Fact]
        public async Task RequestMadeByAnotherUser_Returns404()
        {
            var record = ToolApprovalHistoryTests.Record();
            record.UserId = "user-a";
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            _userContext.Guid = "user-b";

            var result = await _processor.DecideAsync(Command(), 5, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.NotFound, result.ResultType);
        }

        [Fact]
        public async Task PublicHandler_ResolvesAgentFromShareId()
        {
            Decides(ToolApprovalDecisionOutcome.Decided, ToolApprovalHistoryTests.Record("Approved", "AllowOnce"));
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { Id = 1, ShareId = "share-1", AgentId = 5 },
                new() { Id = 2, ShareId = "share-2", AgentId = 6 }
            }.BuildMockDbSet().Object);
            var handler = new DecidePublicToolApprovalCommandHandler(_processor, db.Object);

            var ok = await handler.Handle(new DecidePublicToolApprovalCommand { ShareId = "share-1", ApprovalId = "approval-1", SessionId = "session-1", Decision = "AllowOnce" }, TestContext.Current.CancellationToken);
            var wrongAgent = await handler.Handle(new DecidePublicToolApprovalCommand { ShareId = "share-2", ApprovalId = "approval-1", SessionId = "session-1", Decision = "AllowOnce" }, TestContext.Current.CancellationToken);
            var unknownShare = await handler.Handle(new DecidePublicToolApprovalCommand { ShareId = "nope", ApprovalId = "approval-1", SessionId = "session-1", Decision = "AllowOnce" }, TestContext.Current.CancellationToken);

            Assert.Equal(ResultType.Ok, ok.ResultType);
            Assert.Equal(ResultType.NotFound, wrongAgent.ResultType);
            Assert.Equal(ResultType.NotFound, unknownShare.ResultType);
        }
    }
}
