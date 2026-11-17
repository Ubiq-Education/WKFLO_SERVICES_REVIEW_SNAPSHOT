using AMAIS.WKFLO.AI.Application.Agents.Actors;
using AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.PublishAgentEvent
{
    public class PublishAgentEventCommandHandlerTests
    {
        private readonly Mock<DaprClient> _daprClientMock;
        private readonly Mock<ISessionWorkingStateStore> _workingStateMock = new();
        private readonly PublishAgentEventCommandHandler _handler;

        private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        public PublishAgentEventCommandHandlerTests()
        {
            _daprClientMock = new Mock<DaprClient>();
            _handler = new PublishAgentEventCommandHandler(
                _daprClientMock.Object,
                new TenantContext(),
                new ChatContext(),
                new UserContext(),
                new AgentSharableContext(),
                Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now),
                NullLogger<PublishAgentEventCommandHandler>.Instance,
                _workingStateMock.Object);
        }

        /// <summary>
        /// Review fix (thread 97618): a hand-off requested by a turn is that turn's. A Stop that lands after the turn's
        /// checkpoint but before this publish is later than the publish time, yet earlier than the turn's start is not:
        /// with the turn's start as queue time, the actor guard (and the activity checkpoint) drop the hand-off.
        /// </summary>
        [Fact]
        public async Task Handle_RequestedByATurn_IsQueuedWithTheTurnsStart_SoAStopDuringThePublishDropsIt()
        {
            var turnStartedAt = Now.AddSeconds(-30);
            var stoppedAt = Now.AddSeconds(-1); // after the plugin's checkpoint read, before this publish
            WkfloQueueMessage<AgentEventMessage> captured = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, data, _, _, _) => captured = data)
                .Returns(Task.CompletedTask);
            var command = BuildCommand();
            command.RequestedByTurnStartedAt = turnStartedAt;

            await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.Equal(turnStartedAt, captured.QueuedAt);
            Assert.True(AgentActorGatekeeper.IsQueuedBeforeStop(captured.QueuedAt, stoppedAt));
        }

        [Fact]
        public async Task Handle_NotRequestedByATurn_IsQueuedNow()
        {
            WkfloQueueMessage<AgentEventMessage> captured = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, data, _, _, _) => captured = data)
                .Returns(Task.CompletedTask);

            await _handler.Handle(BuildCommand(), TestContext.Current.CancellationToken);

            Assert.Equal(Now, captured.QueuedAt);
        }

        [Fact]
        public async Task Handle_TurnAlreadyStopped_QueuesNothing()
        {
            using var stopped = new CancellationTokenSource();
            stopped.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await _handler.Handle(BuildCommand(), stopped.Token));

            _daprClientMock.Verify(x => x.SaveStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_StoppedBetweenTheSaveAndThePublish_RemovesTheSavedPayload()
        {
            using var stop = new CancellationTokenSource();
            string savedKey = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, key, _, _, _, _) => { savedKey = key; stop.Cancel(); })
                .Returns(Task.CompletedTask);
            _daprClientMock
                .Setup(x => x.PublishEventAsync(CommonConsts.CommonAiPubSubConsts.PubSubName, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, string, CancellationToken>((_, _, _, ct) => Task.FromCanceled(ct));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await _handler.Handle(BuildCommand(), stop.Token));

            Assert.NotNull(savedKey);
            _daprClientMock.Verify(x => x.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, savedKey,
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.Is<CancellationToken>(t => !t.CanBeCanceled)), Times.Once);
        }

        /// <summary>
        /// US 7506: a published hand-off keeps the session working while it waits in the agent's queue. The entry is
        /// written before the publish, so a hand-off that runs (and ends) at once can never leave it behind.
        /// </summary>
        [Fact]
        public async Task Handle_MarksTheSessionWorking_BeforePublishing()
        {
            string savedKey = null;
            var order = new List<string>();
            _daprClientMock
                .Setup(x => x.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, key, _, _, _, _) => savedKey = key)
                .Returns(Task.CompletedTask);
            _workingStateMock
                .Setup(x => x.MarkWorkingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<TimeSpan>()))
                .Callback(() => order.Add("mark"))
                .Returns(Task.CompletedTask);
            _daprClientMock
                .Setup(x => x.PublishEventAsync(CommonConsts.CommonAiPubSubConsts.PubSubName, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("publish"))
                .Returns(Task.CompletedTask);

            await _handler.Handle(BuildCommand(), TestContext.Current.CancellationToken);

            _workingStateMock.Verify(x => x.MarkWorkingAsync(It.IsAny<string>(), "session-1", savedKey, Now, SessionWorkingStateConsts.HandOffTimeToLive), Times.Once);
            Assert.Equal(["mark", "publish"], order);
        }

        [Fact]
        public async Task Handle_StoppedBeforeThePublish_ClearsTheWorkingEntry()
        {
            using var stop = new CancellationTokenSource();
            string savedKey = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, key, _, _, _, _) => { savedKey = key; stop.Cancel(); })
                .Returns(Task.CompletedTask);
            _daprClientMock
                .Setup(x => x.PublishEventAsync(CommonConsts.CommonAiPubSubConsts.PubSubName, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, string, CancellationToken>((_, _, _, ct) => Task.FromCanceled(ct));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await _handler.Handle(BuildCommand(), stop.Token));

            _workingStateMock.Verify(x => x.ClearWorkingAsync(It.IsAny<string>(), "session-1",
                It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && System.Linq.Enumerable.Contains(ids, savedKey))), Times.Once);
        }

        [Fact]
        public async Task Handle_PublishFails_ClearsTheWorkingEntry_AndRethrows()
        {
            string savedKey = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, key, _, _, _, _) => savedKey = key)
                .Returns(Task.CompletedTask);
            _daprClientMock
                .Setup(x => x.PublishEventAsync(CommonConsts.CommonAiPubSubConsts.PubSubName, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("broker unavailable"));

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await _handler.Handle(BuildCommand(), TestContext.Current.CancellationToken));

            _workingStateMock.Verify(x => x.ClearWorkingAsync(It.IsAny<string>(), "session-1",
                It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && System.Linq.Enumerable.Contains(ids, savedKey))), Times.Once);
        }

        private static PublishAgentEventCommand BuildCommand(List<FileAttachment> files = null, bool? rootIsWidget = null) => new()
        {
            Id = 7,
            AgentRole = "watcher",
            Input = new AgentInvocationInput { Text = "react", Files = files },
            SessionId = "session-1",
            EventType = "task-completed",
            RootIsWidget = rootIsWidget
        };

        [Fact]
        public async Task Handle_SmallPayload_SavesStateAndPublishesEvent()
        {
            var command = BuildCommand();

            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<bool>>(result);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_CommandHasRootIsWidget_ForwardsItOntoQueueMessageCommand()
        {
            // RootIsWidget was resolved by AgentEventPublisherPlugin while still in the calling agent's
            // own turn (see PublishAgentEventDomainEventHandler) — this handler must carry it through
            // onto the queued AgentEventMessage (its only real consumer) so AgentEventWorkflowActivity
            // can restore it on the receiving (subagent) side.
            var command = BuildCommand(rootIsWidget: false);

            WkfloQueueMessage<AgentEventMessage> captured = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, data, _, _, _) => captured = data)
                .Returns(Task.CompletedTask);

            await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.NotNull(captured);
            Assert.False(captured.Command.RootIsWidget);
        }

        [Fact]
        public async Task Handle_CommandHasApprovalRoot_ForwardsItOntoQueueMessageCommand()
        {
            var command = BuildCommand();
            command.ApprovalRootAgentId = 3;

            WkfloQueueMessage<AgentEventMessage> captured = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, data, _, _, _) => captured = data)
                .Returns(Task.CompletedTask);

            await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.Equal(3, captured.Command.ApprovalRootAgentId);
        }

        [Fact]
        public void ApprovalRoot_CannotBeSetFromAnHttpRequestBody()
        {
            // The public publish endpoint binds this command from JSON: nobody outside may turn approvals on.
            var command = Newtonsoft.Json.JsonConvert.DeserializeObject<PublishAgentEventCommand>(
                "{\"id\":7,\"agentRole\":\"watcher\",\"sessionId\":\"s\",\"eventType\":\"e\",\"approvalRootAgentId\":3}");

            Assert.Null(command.ApprovalRootAgentId);
        }

        [Fact]
        public async Task Handle_AttachmentsExceedMaxSize_ReturnsInvalidResultWithoutSavingState()
        {
            var oversizedFiles = new List<FileAttachment>
            {
                new() { FileName = "big.png", FileData = new string('a', 19_000_000) }
            };
            var command = BuildCommand(oversizedFiles);

            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.IsType<InvalidResult>(result);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _daprClientMock.Verify(x => x.PublishEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_SingleImageExceedsMaxSize_ReturnsInvalidResultWithoutSavingState()
        {
            // Under the aggregate MaxQueueMessageSizeBytes but over the per-image
            // MaxSingleImageSizeBytes (our stricter 5 MB business limit, well below
            // Anthropic's own 10 MB hard cap) — story #6670.
            var oversizedImage = new List<FileAttachment>
            {
                new() { FileName = "big.png", FileData = $"data:image/png;base64,{new string('A', 14_000_000)}" }
            };
            var command = BuildCommand(oversizedImage);

            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.IsType<InvalidResult>(result);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_StampsWhenTheHandOffWasQueued()
        {
            // US 7501: a Stop cancels the hand-offs queued before it, even when they only run later.
            WkfloQueueMessage<AgentEventMessage> captured = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<AgentEventMessage>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<AgentEventMessage>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, data, _, _, _) => captured = data)
                .Returns(Task.CompletedTask);

            await _handler.Handle(BuildCommand(), TestContext.Current.CancellationToken);

            Assert.Equal(Now, captured.QueuedAt);
        }
    }
}
