using AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById;
using AMAIS.WKFLO.AI.Application.Agents.Commands.SaveStoppedQueuedTurn;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Common.Activities
{
    /// <summary>
    /// US 7501: how the chat workflow activities (BaseWorkflowActivity) handle a Stop - the terminal "cancelled" status,
    /// no resume, the queued message deleted, the chat back to idle - and that a real timeout keeps today's behaviour.
    /// </summary>
    public class BaseWorkflowActivityStopTests
    {
        private const string MessageKey = "msg:instance-1";
        private const string IdempotencyKey = "v2-MessageTenantAgentByIdCommand-wf-1-session-1";
        private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<DaprClient> _dapr = new();
        private readonly Mock<IWkfloMediator> _mediator = new();
        private readonly Mock<IChatStopStateStore> _stopState = new();
        private readonly Mock<IChatNotificationService> _notifications = new();
        private readonly ChatTurnRegistry _registry = new();
        private readonly MessageTenantAgentByIdWorkflowActivity _activity;
        private readonly WorkflowActivityContext _context;

        public BaseWorkflowActivityStopTests()
        {
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var checkpoint = new ChatStopCheckpoint(_stopState.Object, _registry, NullLogger<ChatStopCheckpoint>.Instance);

            _activity = new MessageTenantAgentByIdWorkflowActivity(NullLogger<MessageTenantAgentByIdWorkflowActivity>.Instance, _mediator.Object,
                WkfloMapperExtensions.CreateMapper(), dateTimeProvider, new TenantContext(), new ChatContext(), new UserContext(), new AgentSharableContext(),
                _dapr.Object, _registry, checkpoint, _notifications.Object);

            var context = new Mock<WorkflowActivityContext>();
            context.SetupGet(x => x.InstanceId).Returns("wf-1");
            _context = context.Object;
        }

        private void QueuedMessage(DateTimeOffset? queuedAt) =>
            _dapr.Setup(x => x.GetStateAsync<WkfloQueueMessage<MessageTenantAgentByIdCommand>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, MessageKey, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WkfloQueueMessage<MessageTenantAgentByIdCommand>
                {
                    Command = new MessageTenantAgentByIdCommand { Id = 5, SessionId = "session-1", Input = new MessageTenantAgentInputCommand { Text = "hi" } },
                    InstanceId = "instance-1",
                    TenantContext = new TenantContext { TenantName = "tenant-a" },
                    ChatContext = new ChatContext(),
                    UserContext = new UserContext(),
                    AgentSharableContext = new AgentSharableContext(),
                    QueuedAt = queuedAt
                });

        private void Idempotency(string status, DateTimeOffset updatedAt) =>
            _dapr.Setup(x => x.GetStateAsync<IdempotencyState>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, IdempotencyKey, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IdempotencyState { Status = status, UpdatedAt = updatedAt });

        private void VerifyStatusSaved(string status, Func<Times> times) =>
            _dapr.Verify(x => x.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, IdempotencyKey,
                It.Is<IdempotencyState>(s => s.Status == status), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), times);

        private void VerifyMessageDeleted(Func<Times> times) =>
            _dapr.Verify(x => x.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, MessageKey,
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), times);

        [Fact]
        public async Task StopDuringTheTurn_MarksItCancelled_DeletesTheMessage_AndSendsIdle()
        {
            QueuedMessage(Now.AddSeconds(-2));
            _mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .Returns(async (IWkfloRequest<Result> _, CancellationToken token) =>
                {
                    // The Stop arrives (from the broadcast) while the agent is running.
                    _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now);
                    await Task.Delay(Timeout.Infinite, token);
                    return new SuccessResult<bool>(true);
                });

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.True(result);
            VerifyStatusSaved(MessageTenantAgentByIdWorkflowActivity.CancelledStatus, Times.Once);
            VerifyStatusSaved("failed", Times.Never);
            VerifyStatusSaved("done", Times.Never);
            VerifyMessageDeleted(Times.Once);
            _notifications.Verify(x => x.SendTypingIndicator("session-1", false, null, null, null), Times.Once);
            _notifications.Verify(x => x.SendConversationStateUpdate("session-1", "idle", null), Times.Once);
        }

        /// <summary>Stop then Send: the new message already runs, so the stopped turn's late typing-off/idle would hide it.</summary>
        [Fact]
        public async Task StopDuringTheTurn_NewerTurnAlreadyRunning_DoesNotSendIdleAgain()
        {
            QueuedMessage(Now.AddSeconds(-2));
            IChatTurn newerTurn = null;
            _mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .Returns(async (IWkfloRequest<Result> _, CancellationToken token) =>
                {
                    _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now);
                    newerTurn = _registry.Register("tenant-a", "session-1", Now.AddSeconds(1));
                    await Task.Delay(Timeout.Infinite, token);
                    return new SuccessResult<bool>(true);
                });

            var result = await _activity.RunAsync(_context, MessageKey);
            newerTurn?.Dispose();

            Assert.True(result);
            VerifyStatusSaved(MessageTenantAgentByIdWorkflowActivity.CancelledStatus, Times.Once);
            VerifyMessageDeleted(Times.Once);
            _notifications.Verify(x => x.SendTypingIndicator(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _notifications.Verify(x => x.SendConversationStateUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
        }

        [Fact]
        public async Task MessageQueuedBeforeAStop_IsCancelledAtTurnStart_WithoutRunningTheAgent()
        {
            QueuedMessage(Now.AddSeconds(-30));
            _stopState.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(Now.AddSeconds(-10));

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.True(result);
            _mediator.Verify(x => x.Send(It.IsAny<MessageTenantAgentByIdCommand>(), It.IsAny<CancellationToken>()), Times.Never);
            VerifyStatusSaved(MessageTenantAgentByIdWorkflowActivity.CancelledStatus, Times.Once);
            VerifyMessageDeleted(Times.Once);

            // US 7502: the turn never ran, so its "Stopped" marker (only the marker) is saved for it, exactly once.
            _mediator.Verify(x => x.Send(It.Is<SaveStoppedQueuedTurnCommand>(c =>
                    c.AgentId == 5 && c.ShareId == null && c.SessionId == "session-1"),
                It.IsAny<CancellationToken>()), Times.Once);
            _mediator.Verify(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task StopDuringTheTurn_DoesNotSaveAQueuedTurn_TheRunSavesItself()
        {
            QueuedMessage(Now.AddSeconds(-2));
            _mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .Returns(async (IWkfloRequest<Result> _, CancellationToken token) =>
                {
                    _registry.CancelTurnsStartedBefore("tenant-a", "session-1", Now);
                    await Task.Delay(Timeout.Infinite, token);
                    return new SuccessResult<bool>(true);
                });

            await _activity.RunAsync(_context, MessageKey);

            _mediator.Verify(x => x.Send(It.IsAny<SaveStoppedQueuedTurnCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MessageQueuedAfterAStop_RunsNormally()
        {
            QueuedMessage(Now.AddSeconds(-1));
            _stopState.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(Now.AddSeconds(-10));
            _mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SuccessResult<bool>(true));

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.True(result);
            _mediator.Verify(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.Is<CancellationToken>(t => t.CanBeCanceled)), Times.Once);
            VerifyStatusSaved("done", Times.Once);
            VerifyStatusSaved(MessageTenantAgentByIdWorkflowActivity.CancelledStatus, Times.Never);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(60)]
        public async Task CancelledTurn_IsNeverResumed_EvenWhenOlderThanTheStaleWindow(int minutesAgo)
        {
            QueuedMessage(Now.AddMinutes(-minutesAgo));
            Idempotency(MessageTenantAgentByIdWorkflowActivity.CancelledStatus, Now.AddMinutes(-minutesAgo));

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.True(result);
            _mediator.Verify(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()), Times.Never);
            VerifyStatusSaved("processing", Times.Never);
        }

        [Fact]
        public async Task TimeoutThatIsNotAStop_KeepsTheFailedPath()
        {
            QueuedMessage(Now.AddSeconds(-2));
            _mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException("provider timeout"));

            var result = await _activity.RunAsync(_context, MessageKey);

            Assert.False(result);
            VerifyStatusSaved("failed", Times.Once);
            VerifyStatusSaved(MessageTenantAgentByIdWorkflowActivity.CancelledStatus, Times.Never);
            VerifyMessageDeleted(Times.Never);
            _notifications.Verify(x => x.SendConversationStateUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
        }
    }
}
