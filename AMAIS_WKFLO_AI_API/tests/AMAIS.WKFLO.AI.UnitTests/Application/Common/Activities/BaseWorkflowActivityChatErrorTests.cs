using AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.SignalR;
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Common.Activities
{
    /// <summary>
    /// US 7959 / Task 7961: BaseWorkflowActivity is the single place a single-agent text/voice turn's failure reaches the
    /// chat - once per turn, never for a Stop, never after the answer was delivered, with the failing agent's typing off
    /// and never any exception text. Runs the real notifier, failure state and reporter.
    /// </summary>
    public class BaseWorkflowActivityChatErrorTests
    {
        private const string MessageKey = "msg:wf-1";
        private const string InstanceId = "wf-1";
        private const string IdempotencyKey = "v2-MessageTenantAgentByIdCommand-wf-1-session-1";
        private const string Sentinel = "SENTINEL-exception-secret";
        private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset QueuedAt = Now.AddSeconds(-2);

        private readonly Mock<DaprClient> _dapr = new();
        private readonly Mock<IWkfloMediator> _mediator = new();
        private readonly Mock<IChatStopStateStore> _stopState = new();
        private readonly ChatNotificationRecorder _chat = new();
        private readonly ChatTurnRegistry _registry = new();
        private readonly ChatTurnFailureState _failureState = new();
        private readonly ChatTurnErrorReporter _reporter;
        private readonly MessageTenantAgentByIdWorkflowActivity _activity;
        private readonly WorkflowActivityContext _context;

        public BaseWorkflowActivityChatErrorTests()
        {
            var clock = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var checkpoint = new ChatStopCheckpoint(_stopState.Object, _registry, NullLogger<ChatStopCheckpoint>.Instance);
            var notifier = new ChatTurnErrorNotifier(_chat.Object, _stopState.Object, _dapr.Object, clock, NullLogger<ChatTurnErrorNotifier>.Instance);
            _reporter = new ChatTurnErrorReporter(notifier, _failureState, NullLogger<ChatTurnErrorReporter>.Instance);

            _activity = new MessageTenantAgentByIdWorkflowActivity(NullLogger<MessageTenantAgentByIdWorkflowActivity>.Instance, _mediator.Object,
                WkfloMapperExtensions.CreateMapper(), clock, new TenantContext(), new ChatContext(), new UserContext(), new AgentSharableContext(),
                _dapr.Object, _registry, checkpoint, _chat.Object, notifier, _failureState, _reporter);

            var context = new Mock<WorkflowActivityContext>();
            context.SetupGet(x => x.InstanceId).Returns(InstanceId);
            _context = context.Object;

            _dapr.Setup(x => x.GetStateAsync<WkfloQueueMessage<MessageTenantAgentByIdCommand>>(
                    StateStoreName, MessageKey, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WkfloQueueMessage<MessageTenantAgentByIdCommand>
                {
                    Command = new MessageTenantAgentByIdCommand { Id = 5, SessionId = "session-1", Input = new MessageTenantAgentInputCommand { Text = "hi" } },
                    InstanceId = InstanceId,
                    TenantContext = new TenantContext { TenantName = "tenant-a" },
                    ChatContext = new ChatContext(),
                    UserContext = new UserContext(),
                    AgentSharableContext = new AgentSharableContext(),
                    QueuedAt = QueuedAt
                });

            // The dedupe claim of the notifier succeeds (first report of the turn).
            _dapr.Setup(x => x.TrySaveStateAsync(StateStoreName, It.IsAny<string>(), It.IsAny<ChatTurnErrorClaim>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        private static string StateStoreName => CommonAiPubSubConsts.StateStoreName;

        private void MediatorReturns(Result result, Action during = null) =>
            _mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .Returns((IWkfloRequest<Result> _, CancellationToken _) =>
                {
                    during?.Invoke();
                    return Task.FromResult(result);
                });

        private void MediatorThrows(Exception exception, Action during = null) =>
            _mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .Returns((IWkfloRequest<Result> _, CancellationToken _) =>
                {
                    during?.Invoke();
                    return Task.FromException<Result>(exception);
                });

        private void VerifyStatusSaved(string status, Func<Times> times) =>
            _dapr.Verify(x => x.SaveStateAsync(StateStoreName, IdempotencyKey, It.Is<IdempotencyState>(s => s.Status == status),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), times);

        private MessageUpdate SingleMarker() => Assert.Single(_chat.Markers());

        private static object Meta(MessageUpdate marker, string key) => marker.Message.Metadata.TryGetValue(key, out var value) ? value : null;

        private List<ConversationStateUpdate> States() => _chat.Sent
            .Where(s => s.Method == HubReceiverMethods.ConversationStateUpdate)
            .Select(s => (ConversationStateUpdate)s.Payload)
            .ToList();

        [Fact]
        public async Task WorkerValidationFails_ReportsAgentUnavailable_WithThePublicDetailCode()
        {
            MediatorReturns(new InvalidResult("Id", "WKFLOAI0302"));

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.True(result);
            var marker = SingleMarker();
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, Meta(marker, CommonMetadataConsts.ErrorCode));
            Assert.Equal("WKFLOAI0302", Meta(marker, CommonMetadataConsts.DetailCode));
            Assert.Equal(true, Meta(marker, CommonMetadataConsts.IsTerminal));
            Assert.Equal(InstanceId, Meta(marker, CommonMetadataConsts.TurnId));
            Assert.Equal("session-1", marker.ChatId);
            var state = Assert.Single(States());
            Assert.Equal("error", state.State);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, state.Metadata[CommonMetadataConsts.ErrorCode]);
            VerifyStatusSaved("done", Times.Once);
        }

        [Fact]
        public async Task WorkerValidationFails_WithANonPublicCode_SendsNoDetailCode()
        {
            MediatorReturns(new InvalidResult("Input", "WKFLOAI0001"));

            await _activity.RunAsync(_context, MessageKey);

            var marker = SingleMarker();
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, Meta(marker, CommonMetadataConsts.ErrorCode));
            Assert.Null(Meta(marker, CommonMetadataConsts.DetailCode));
        }

        [Fact]
        public async Task NoAnswer_WithAFailureTheRunRecorded_ReportsThatFailure_OfThatAgent()
        {
            MediatorReturns(new NotFoundResult(), () => _failureState.Record(ChatErrorCodes.ChatModelRateLimited, 5, "Support Bot", "https://icons/bot.png"));

            await _activity.RunAsync(_context, MessageKey);

            var marker = SingleMarker();
            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, Meta(marker, CommonMetadataConsts.ErrorCode));
            Assert.Equal(5, marker.AgentId);
            Assert.Equal("https://icons/bot.png", marker.ChatbotIconUrl);

            // Typing off for the failing agent first, then the marker, then the "error" state - and nothing turns it back on.
            Assert.Equal([HubReceiverMethods.TypingIndicator, HubReceiverMethods.ReceiveMessage, HubReceiverMethods.ConversationStateUpdate],
                _chat.Sent.Select(s => s.Method));
            Assert.Equal([false], _chat.TypingOf(5));
        }

        [Fact]
        public async Task SuccessResult_WithARecordedStreamingFailure_IsStillReported()
        {
            MediatorReturns(new SuccessResult<bool>(true), () => _failureState.Record(ChatErrorCodes.ChatModelUnavailable, 5, "Support Bot"));

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.True(result);
            Assert.Equal(ChatErrorCodes.ChatModelUnavailable, Meta(SingleMarker(), CommonMetadataConsts.ErrorCode));
            VerifyStatusSaved("done", Times.Once);
        }

        [Fact]
        public async Task SuccessResult_WithNothingRecorded_SendsNothing()
        {
            MediatorReturns(new SuccessResult<bool>(true));

            await _activity.RunAsync(_context, MessageKey);

            Assert.Empty(_chat.Sent);
        }

        [Fact]
        public async Task RunThrows_ReportsTheClassifiedCode_MarksFailed_AndNeverSendsTheExceptionText()
        {
            MediatorThrows(new HttpRequestException(Sentinel));

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.False(result);
            Assert.Equal(ChatErrorCodes.ChatModelUnavailable, Meta(SingleMarker(), CommonMetadataConsts.ErrorCode));
            Assert.Equal("error", Assert.Single(States()).State);
            VerifyStatusSaved("failed", Times.Once);
            _chat.AssertNoLeak(Sentinel, nameof(HttpRequestException), "StackTrace", "   at ");
        }

        [Fact]
        public async Task RunThrows_AfterARecordedFailure_ReportsTheRecordedOne_Once()
        {
            MediatorThrows(new InvalidOperationException(Sentinel), () => _failureState.Record(ChatErrorCodes.ChatAttachmentFailed, 5, "Support Bot"));

            await _activity.RunAsync(_context, MessageKey);

            Assert.Equal(ChatErrorCodes.ChatAttachmentFailed, Meta(SingleMarker(), CommonMetadataConsts.ErrorCode));
            _chat.AssertNoLeak(Sentinel);
        }

        [Fact]
        public async Task CancellationThatIsNotAStop_IsReportedAsATimeout()
        {
            MediatorThrows(new TaskCanceledException("provider timeout"));

            await _activity.RunAsync(_context, MessageKey);

            Assert.Equal(ChatErrorCodes.ChatModelTimeout, Meta(SingleMarker(), CommonMetadataConsts.ErrorCode));
            VerifyStatusSaved("failed", Times.Once);
        }

        [Fact]
        public async Task StoppedByTheUser_NeverReportsAnError_EvenWithARecordedFailure()
        {
            _mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .Returns(async (IWkfloRequest<Result> _, CancellationToken token) =>
                {
                    _failureState.Record(ChatErrorCodes.ChatModelUnavailable, 5, "Support Bot");
                    _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now);
                    await Task.Delay(Timeout.Infinite, token);
                    return new SuccessResult<bool>(true);
                });

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.True(result);
            Assert.Empty(_chat.Markers());
            Assert.DoesNotContain(States(), s => s.State == "error");
            Assert.Equal("idle", Assert.Single(States()).State);
        }

        /// <summary>A Stop swallowed by a lower layer (the run returns no answer) is still no error.</summary>
        [Fact]
        public async Task StoppedByTheUser_RunReturnsNoAnswer_NeverReportsAnError()
        {
            MediatorReturns(new NotFoundResult(), () => _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now));

            await _activity.RunAsync(_context, MessageKey);

            Assert.Empty(_chat.Markers());
            Assert.DoesNotContain(States(), s => s.State == "error");
        }

        [Fact]
        public async Task StopRecordedInTheStateStoreAfterTheTurnStarted_SuppressesTheError()
        {
            MediatorReturns(new NotFoundResult(), () =>
                _stopState.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(Now));

            await _activity.RunAsync(_context, MessageKey);

            Assert.Empty(_chat.Markers());
        }

        [Fact]
        public async Task TerminalFailure_AfterTheAnswerWasDelivered_IsNotReported_ButTheChatLeavesProcessing()
        {
            MediatorReturns(new NotFoundResult(), () =>
            {
                _failureState.MarkAnswerDelivered();
                _failureState.Record(ChatErrorCodes.ChatInfrastructureError, 5, "Support Bot");
            });

            await _activity.RunAsync(_context, MessageKey);

            Assert.Empty(_chat.Markers());
            Assert.Equal([false], _chat.TypingOf(5));
            Assert.Equal("idle", Assert.Single(States()).State);
        }

        [Fact]
        public async Task NonTerminalError_AfterTheAnswerWasDelivered_IsReported_WithoutEndingTheTurnAsAnError()
        {
            MediatorReturns(new SuccessResult<bool>(true), () =>
            {
                _failureState.MarkAnswerDelivered();
                _failureState.Record(ChatTurnFailure.Create(ChatErrorCodes.ChatVoiceSynthesisFailed, 5, "Support Bot", isTerminal: false));
            });

            await _activity.RunAsync(_context, MessageKey);

            var marker = SingleMarker();
            Assert.Equal(ChatErrorCodes.ChatVoiceSynthesisFailed, Meta(marker, CommonMetadataConsts.ErrorCode));
            Assert.Equal(false, Meta(marker, CommonMetadataConsts.IsTerminal));
            Assert.Empty(States());
        }

        [Fact]
        public async Task OnlyAWarningRecorded_TheChokePointSendsNothing()
        {
            MediatorReturns(new SuccessResult<bool>(true), () => _failureState.Record(ChatErrorCodes.ChatToolFailed, 5));

            await _activity.RunAsync(_context, MessageKey);

            Assert.Empty(_chat.Sent);
        }

        [Fact]
        public async Task ToolWarningDuringTheTurn_IsSentLive_ForThisTurn()
        {
            MediatorReturns(new SuccessResult<bool>(true), () =>
            {
                _reporter.SetCurrentAgent(new ChatTurnAgent(5, "Support Bot"));
                _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolUnavailable, "plugin_12_MCP").GetAwaiter().GetResult();
            });

            await _activity.RunAsync(_context, MessageKey);

            var marker = SingleMarker();
            Assert.Equal(ChatErrorCodes.ChatToolUnavailable, Meta(marker, CommonMetadataConsts.ErrorCode));
            Assert.Equal(ChatErrorSeverity.Warning, Meta(marker, CommonMetadataConsts.Severity));
            Assert.Equal(InstanceId, Meta(marker, CommonMetadataConsts.TurnId));
            Assert.Empty(States());
        }

        [Fact]
        public async Task FailureWithoutAnAgent_IsReportedForTheAgentThatWasAnswering()
        {
            MediatorThrows(new HttpRequestException(Sentinel), () => _reporter.SetCurrentAgent(new ChatTurnAgent(5, "Support Bot", "https://icons/bot.png")));

            await _activity.RunAsync(_context, MessageKey);

            var marker = SingleMarker();
            Assert.Equal(5, marker.AgentId);
            Assert.Equal([false], _chat.TypingOf(5));
        }

        [Fact]
        public async Task TurnAlreadyFailed_IsNotReportedAgain()
        {
            _dapr.Setup(x => x.GetStateAsync<IdempotencyState>(StateStoreName, IdempotencyKey, It.IsAny<ConsistencyMode?>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IdempotencyState { Status = "failed", UpdatedAt = Now });

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.False(result);
            Assert.Empty(_chat.Sent);
            _mediator.Verify(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AlreadyReportedByAnotherAttempt_IsNotSentTwice()
        {
            _dapr.Setup(x => x.TrySaveStateAsync(StateStoreName, It.IsAny<string>(), It.IsAny<ChatTurnErrorClaim>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            MediatorReturns(new NotFoundResult());

            await _activity.RunAsync(_context, MessageKey);

            Assert.Empty(_chat.Markers());
        }
    }
}
