using AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent;
using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.AI.Application.Agents.Invocation;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.SignalR;
using Dapr.Client;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Commands.PublishAgentEvent
{
    public class AgentEventWorkflowActivityTests
    {
        private readonly Mock<DaprClient> _daprClientMock;
        private readonly Mock<IAgentInvoker> _agentInvokerMock;
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly AgentEventWorkflowActivity _activity;
        private readonly ChatTurnRegistry _turnRegistry = new();
        private readonly Mock<IChatStopStateStore> _stopStateStoreMock = new();
        private readonly Mock<IDroppedHandOffMarkerSaver> _markerSaverMock = new();
        private static readonly DateTimeOffset StopTime = new(2026, 10, 19, 11, 59, 0, TimeSpan.Zero);
        private readonly Mock<IChatStopCheckpoint> _stopCheckpointMock = new();

        private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private const string Key = "msg:instance-1";

        public AgentEventWorkflowActivityTests()
        {
            _daprClientMock = new Mock<DaprClient>();
            _agentInvokerMock = new Mock<IAgentInvoker>();

            _dbMock = new Mock<IMasterDataDbContext>();
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            _notificationServiceMock = new Mock<IChatNotificationService>();
            _notificationServiceMock
                .Setup(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            _activity = new AgentEventWorkflowActivity(_daprClientMock.Object, _agentInvokerMock.Object, _dbMock.Object, _notificationServiceMock.Object, NullLogger<AgentEventWorkflowActivity>.Instance,
                _turnRegistry, _stopCheckpointMock.Object, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now),
                _stopStateStoreMock.Object, _markerSaverMock.Object, workingState: _workingStateMock.Object);
        }

        private readonly Mock<ISessionWorkingStateStore> _workingStateMock = new();

        private static WkfloQueueMessage<AgentEventMessage> BuildQueueMessage(int agentId = 7, bool? rootIsWidget = null) => new()
        {
            Command = new AgentEventMessage
            {
                AgentId = agentId,
                AgentRole = "watcher",
                Input = new AgentInvocationInput { Text = "react" },
                SessionId = "session-1",
                EventType = "task-completed",
                RootIsWidget = rootIsWidget
            },
            TenantContext = new TenantContext { TenantName = "tenant-a" },
            ChatContext = new ChatContext(),
            UserContext = new UserContext(),
            AgentSharableContext = new AgentSharableContext()
        };

        [Fact]
        public async Task RunAsync_ValidQueuedEvent_InvokesAgentAndDeletesState()
        {
            var queueMessage = BuildQueueMessage();

            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = true, Content = "done" });

            var result = await _activity.RunAsync(context: null, Key);

            Assert.True(result);
            _agentInvokerMock.Verify(x => x.InvokeAsync(
                It.Is<AgentInvocationRequest>(r => r.AgentId == 7 && r.AgentRole == "watcher" && r.SessionId == "session-1"),
                It.IsAny<CancellationToken>()), Times.Once);
            _daprClientMock.Verify(x => x.DeleteStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<StateOptions>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_QueuedMessageHasRootIsWidget_ForwardsItOntoAgentInvocationRequest()
        {
            // This is the subagent's receiving side of the spin-up hand-off: a fresh DI scope with no
            // memory of the calling agent's own turn, so the root's widget/non-widget context can only
            // reach IAgentInvoker (and from there, this subagent's own icon resolution) via the queued
            // message — the same way TenantContext/ChatContext/UserContext/AgentSharableContext do.
            var queueMessage = BuildQueueMessage(rootIsWidget: false);

            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = true, Content = "done" });

            await _activity.RunAsync(context: null, Key);

            _agentInvokerMock.Verify(x => x.InvokeAsync(
                It.Is<AgentInvocationRequest>(r => r.RootIsWidget == false),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(null)]
        public async Task RunAsync_ForwardsTheApprovalRootOntoAgentInvocationRequest(int? approvalRootAgentId)
        {
            // Bug 7722: only a hand-off from an interactive chat carries it; the sub-agent run then has approvals.
            var queueMessage = BuildQueueMessage();
            queueMessage.Command.ApprovalRootAgentId = approvalRootAgentId;
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = true, Content = "done" });

            await _activity.RunAsync(context: null, Key);

            _agentInvokerMock.Verify(x => x.InvokeAsync(It.Is<AgentInvocationRequest>(r => r.ApprovalRootAgentId == approvalRootAgentId), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunAsync_RunStoppedOnAnApproval_SendsNoFurtherUpdate()
        {
            // Bug 7722: awaiting_approval was already sent when the approval was requested; not "completed", nothing more.
            var queueMessage = BuildQueueMessage();
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = true, AwaitingApproval = true });

            await _activity.RunAsync(context: null, Key);

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData(true, false, SubagentActivityStatus.Completed)]
        public async Task RunAsync_SendsTheFinalStatus(bool success, bool awaitingApproval, string expectedStatus)
        {
            var queueMessage = BuildQueueMessage();
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = success, AwaitingApproval = awaitingApproval, ErrorMessage = success ? null : "boom" });

            await _activity.RunAsync(context: null, Key);

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate("session-1", 7, "watcher", expectedStatus, It.IsAny<string>(), "task-completed",
                It.IsAny<string>(), success ? null : "boom"), Times.Once);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RunAsync_PassesTheEventTypeToTheSubAgentRunAsItsActivityAction()
        {
            // Bug 7722: a tool approval requested in this run keeps it, so its later states carry the same action.
            var queueMessage = BuildQueueMessage();
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = true });

            await _activity.RunAsync(context: null, Key);

            _agentInvokerMock.Verify(x => x.InvokeAsync(It.Is<AgentInvocationRequest>(r => r.ChatContext.SubagentActivityAction == "task-completed"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunAsync_NoQueuedMessageFound_ReturnsTrueWithoutInvokingAgent()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((WkfloQueueMessage<AgentEventMessage>)null);

            var result = await _activity.RunAsync(context: null, Key);

            Assert.True(result);
            _agentInvokerMock.Verify(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// US 7506: a hand-off that never reaches its turn (the turn removes the queued working-state entry) must remove it
        /// itself, or the session shows as working until the entry expires.
        /// </summary>
        [Fact]
        public async Task RunAsync_QueuedEventHasNoValidAgent_ClearsTheHandOffsWorkingEntry()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildQueueMessage(agentId: 0));

            var result = await _activity.RunAsync(context: null, Key);

            Assert.True(result);
            _agentInvokerMock.Verify(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            _workingStateMock.Verify(x => x.ClearWorkingAsync("tenant-a", "session-1",
                It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains(Key))), Times.Once);
        }

        /// <summary>
        /// Without a payload the session is unknown, so the entry cannot be located (it expires on its own); nothing throws.
        /// </summary>
        [Fact]
        public async Task RunAsync_NoQueuedMessageFound_DoesNotTouchTheWorkingState()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((WkfloQueueMessage<AgentEventMessage>)null);

            var result = await _activity.RunAsync(context: null, Key);

            Assert.True(result);
            _workingStateMock.Verify(x => x.ClearWorkingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }

        [Fact]
        public async Task RunAsync_AgentInvokerReturnsFailure_ReturnsFalseButStillDeletesState()
        {
            var queueMessage = BuildQueueMessage();

            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = false, ErrorMessage = "boom" });

            var result = await _activity.RunAsync(context: null, Key);

            Assert.False(result);
            _daprClientMock.Verify(x => x.DeleteStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<StateOptions>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_AgentInvokerThrows_ReturnsFalseButStillDeletesState()
        {
            var queueMessage = BuildQueueMessage();

            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("sidecar unreachable"));

            var result = await _activity.RunAsync(context: null, Key);

            Assert.False(result);
            _daprClientMock.Verify(x => x.DeleteStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<StateOptions>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_LoadStateThrows_ReturnsFalseWithoutInvokingAgentOrDeletingState()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("state store unavailable"));

            var result = await _activity.RunAsync(context: null, Key);

            Assert.False(result);
            _agentInvokerMock.Verify(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            _daprClientMock.Verify(x => x.DeleteStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_AgentInvokerSucceeds_SendsCompletedSubagentActivityUpdate()
        {
            var queueMessage = BuildQueueMessage();

            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = true, Content = "done" });

            await _activity.RunAsync(context: null, Key);

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                "session-1", 7, "watcher", "completed", It.IsAny<string>(), "task-completed", null, null),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_AgentInvokerReturnsFailure_SendsFailedSubagentActivityUpdateWithItsCodeOnly()
        {
            var queueMessage = BuildQueueMessage();

            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = false, ErrorCode = ChatErrorCodes.ChatModelRateLimited, ErrorMessage = "boom" });

            await _activity.RunAsync(context: null, Key);

            // US 7959 / Task 7963: a code, never the error text.
            _notificationServiceMock.Verify(x => x.SendSubagentActivityFailedUpdate(
                "session-1", 7, "watcher", ChatErrorCodes.ChatModelRateLimited, It.IsAny<string>(), "task-completed", null),
                Times.Once);
            VerifyNoFreeTextFailedUpdate();
        }

        private void VerifyNoFreeTextFailedUpdate() =>
            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), SubagentActivityStatus.Failed,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        [Fact]
        public async Task RunAsync_AgentInvokerThrows_SendsFailedSubagentActivityUpdateWithAClassifiedCode()
        {
            var queueMessage = BuildQueueMessage();

            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("sidecar unreachable"));

            await _activity.RunAsync(context: null, Key);

            _notificationServiceMock.Verify(x => x.SendSubagentActivityFailedUpdate(
                "session-1", 7, "watcher", ChatErrorCodes.ChatTurnFailed, It.IsAny<string>(), "task-completed", null),
                Times.Once);
            VerifyNoFreeTextFailedUpdate();
        }

        [Fact]
        public async Task RunAsync_NoQueuedMessageFound_DoesNotSendSubagentActivityUpdate()
        {
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((WkfloQueueMessage<AgentEventMessage>)null);

            await _activity.RunAsync(context: null, Key);

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_NotificationServiceThrows_StillDeletesStateAndReturnsInvocationOutcome()
        {
            var queueMessage = BuildQueueMessage();

            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = true, Content = "done" });

            _notificationServiceMock
                .Setup(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("hub unavailable"));

            var result = await _activity.RunAsync(context: null, Key);

            Assert.True(result);
            _daprClientMock.Verify(x => x.DeleteStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<StateOptions>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private void SetupQueuedMessage(WkfloQueueMessage<AgentEventMessage> queueMessage) =>
            _daprClientMock
                .Setup(x => x.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(queueMessage);

        [Fact]
        public async Task RunAsync_RegistersTheHandOffAsATurnStartedWhenItWasQueued_AndPassesItsTokenToTheInvoker()
        {
            // US 7501: a hand-off queued before a Stop is cancelled by it, even though it only runs now.
            var queueMessage = BuildQueueMessage();
            queueMessage.QueuedAt = Now.AddMinutes(-1);
            SetupQueuedMessage(queueMessage);

            var cancelledByStop = 0;
            CancellationToken invokerToken = default;
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AgentInvocationRequest, CancellationToken>((_, ct) =>
                {
                    invokerToken = ct;
                    cancelledByStop = _turnRegistry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddSeconds(-30));
                })
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = true });

            await _activity.RunAsync(context: null, Key);

            Assert.Equal(1, cancelledByStop);
            Assert.True(invokerToken.IsCancellationRequested);
            _stopCheckpointMock.Verify(x => x.ThrowIfStoppedAsync("tenant-a", "session-1", ChatStopCheckpoints.HandOffStart, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunAsync_StoppedWhileRunning_SendsCancelledAndDeletesTheState()
        {
            // US 7501: neither "completed" nor "failed"; the stopped run is finished and its msg: state removed.
            SetupQueuedMessage(BuildQueueMessage());
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .Returns<AgentInvocationRequest, CancellationToken>((_, ct) =>
                {
                    _turnRegistry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddSeconds(1));
                    return Task.FromException<AgentInvocationResult>(new OperationCanceledException(ct));
                });

            var result = await _activity.RunAsync(context: null, Key);

            Assert.True(result);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate("session-1", 7, "watcher", SubagentActivityStatus.Cancelled,
                It.IsAny<string>(), "task-completed", It.IsAny<string>(), null), Times.Once);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _daprClientMock.Verify(x => x.DeleteStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<StateOptions>(), It.IsAny<System.Collections.Generic.IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_CheckpointFindsAStop_DoesNotInvokeTheAgentAndSendsCancelled()
        {
            // US 7501: the single read at the start catches a Stop this replica's broadcast subscription missed.
            SetupQueuedMessage(BuildQueueMessage());
            _stopStateStoreMock.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(StopTime);
            _stopCheckpointMock
                .Setup(x => x.ThrowIfStoppedAsync("tenant-a", "session-1", ChatStopCheckpoints.HandOffStart, It.IsAny<CancellationToken>()))
                .Returns<string, string, string, CancellationToken>((tenantName, sessionId, _, ct) =>
                {
                    _turnRegistry.CancelTurnsStartedBefore(tenantName, sessionId, Now.AddSeconds(1));
                    return Task.FromException(new OperationCanceledException(ct));
                });

            var result = await _activity.RunAsync(context: null, Key);

            Assert.True(result);
            _agentInvokerMock.Verify(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate("session-1", 7, "watcher", SubagentActivityStatus.Cancelled,
                It.IsAny<string>(), "task-completed", It.IsAny<string>(), null), Times.Once);

            // US 7502: picked up after the Stop, so it never ran: nothing is saved for Kit (no chat, no conversation);
            // the chat agent's history gets the Stop's marker, through the dropped hand-off rule (one deduped marker).
            _markerSaverMock.Verify(x => x.SaveAsync(It.Is<DroppedHandOffStop>(s =>
                s.TenantName == "tenant-a" && s.SessionId == "session-1" && s.StoppedAt == StopTime && s.DroppedAgentId == 7
                && s.HostAgentId == null && s.HostShareId == null)), Times.Once);
            _agentInvokerMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task RunAsync_StoppedWhileRunning_AlsoGivesTheChatAgentTheStopsMarker()
        {
            // US 7502: a hand-off stopped while running saves only its own pre-turn history plus the marker (when it had
            // one); the chat agent's history gets the Stop's marker too (deduped on stoppedAt), so a hand-off agent with no
            // history of its own still leaves the divider in the chat.
            SetupQueuedMessage(BuildQueueMessage());
            _stopStateStoreMock.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(StopTime);
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .Returns<AgentInvocationRequest, CancellationToken>((_, ct) =>
                {
                    _turnRegistry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddSeconds(1));
                    return Task.FromException<AgentInvocationResult>(new OperationCanceledException(ct));
                });

            await _activity.RunAsync(context: null, Key);

            _markerSaverMock.Verify(x => x.SaveAsync(It.Is<DroppedHandOffStop>(s =>
                s.SessionId == "session-1" && s.StoppedAt == StopTime && s.DroppedAgentId == 7)), Times.Once);
        }

        /// <summary>
        /// Review fix (thread 97618), multi-hop: the hand-off turn runs with its queue time - the requesting turn's start -
        /// as its own turn start, so the hand-offs it requests in turn carry the root turn's start and one Stop drops the chain.
        /// </summary>
        [Fact]
        public async Task RunAsync_PassesItsTurnStartOnToTheHandOffsItRequests()
        {
            var rootTurnStartedAt = Now.AddMinutes(-2);
            var queueMessage = BuildQueueMessage();
            queueMessage.QueuedAt = rootTurnStartedAt;
            SetupQueuedMessage(queueMessage);
            DateTimeOffset? seen = null;
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AgentInvocationRequest, CancellationToken>((request, _) => seen = request.ChatContext?.TurnStartedAt)
                .ReturnsAsync(new AgentInvocationResult { Success = true });

            await _activity.RunAsync(context: null, Key);

            Assert.Equal(rootTurnStartedAt, seen);
        }

        [Fact]
        public async Task RunAsync_CancellationNotCausedByAStop_IsStillAFailure()
        {
            // Unchanged behaviour: only a Stop is reported as cancelled.
            SetupQueuedMessage(BuildQueueMessage());
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("timeout"));

            var result = await _activity.RunAsync(context: null, Key);

            Assert.False(result);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityFailedUpdate("session-1", 7, "watcher", ChatErrorCodes.ChatModelTimeout,
                It.IsAny<string>(), "task-completed", It.IsAny<string>()), Times.Once);
            VerifyNoFreeTextFailedUpdate();
        }

        // --- US 7959 / Task 7963: what the chat receives when a hand-off fails ---

        private (AgentEventWorkflowActivity Activity, ChatErrorHarness Harness) WithChat()
        {
            var harness = new ChatErrorHarness();
            var activity = new AgentEventWorkflowActivity(_daprClientMock.Object, _agentInvokerMock.Object, _dbMock.Object, harness.Chat.Object,
                NullLogger<AgentEventWorkflowActivity>.Instance, harness.TurnRegistry, _stopCheckpointMock.Object,
                Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), _stopStateStoreMock.Object, _markerSaverMock.Object,
                harness.Reporter);
            return (activity, harness);
        }

        [Fact]
        public async Task RunAsync_HandOffFails_StopsItsBubble_SendsANonTerminalMarkerAndItsFailedCard_ThenIdle()
        {
            var queueMessage = BuildQueueMessage();
            queueMessage.QueuedAt = Now.AddSeconds(-5);
            SetupQueuedMessage(queueMessage);
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = false, ErrorCode = ChatErrorCodes.ChatModelUnavailable, ErrorMessage = "SENTINEL 503 from provider" });
            var (activity, harness) = WithChat();

            await activity.RunAsync(context: null, Key);

            Assert.Equal([false], harness.Chat.TypingOf(7));
            var marker = Assert.Single(harness.Chat.Markers());
            Assert.Equal(7, marker.AgentId);
            Assert.Equal(false, marker.Message.Metadata[AMAIS.WKFLO.Common.Models.CommonConsts.CommonMetadataConsts.IsTerminal]);
            var card = Assert.Single(harness.Chat.SubagentUpdatesOf(7));
            Assert.Equal(SubagentActivityStatus.Failed, card.Status);
            Assert.Equal(ChatErrorCodes.ChatModelUnavailable, card.ErrorCode);
            Assert.Equal(["idle"], harness.Chat.States());
            harness.Chat.AssertNoLeak("SENTINEL", "503 from provider");
        }

        [Fact]
        public async Task RunAsync_HandOffThrows_NoExceptionTextReachesTheChat()
        {
            SetupQueuedMessage(BuildQueueMessage());
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SENTINEL secret at Foo.Bar()"));
            var (activity, harness) = WithChat();

            await activity.RunAsync(context: null, Key);

            Assert.Single(harness.Chat.Markers());
            harness.Chat.AssertNoLeak("SENTINEL", "InvalidOperationException", "Foo.Bar");
        }

        [Fact]
        public async Task RunAsync_HandOffStoppedByTheUser_ReportsNoError()
        {
            SetupQueuedMessage(BuildQueueMessage());
            var (activity, harness) = WithChat();
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .Returns<AgentInvocationRequest, CancellationToken>((_, _) =>
                {
                    harness.TurnRegistry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddSeconds(1));
                    return Task.FromException<AgentInvocationResult>(new OperationCanceledException());
                });

            await activity.RunAsync(context: null, Key);

            Assert.Empty(harness.Chat.Markers());
            Assert.DoesNotContain(harness.Chat.SubagentUpdatesOf(7), u => u.Status == SubagentActivityStatus.Failed);
        }

        [Fact]
        public async Task RunAsync_HandOffFailsAfterAStopOfTheSession_ReportsNoError()
        {
            var queueMessage = BuildQueueMessage();
            queueMessage.QueuedAt = Now.AddSeconds(-5);
            SetupQueuedMessage(queueMessage);
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "watcher", Success = false, ErrorCode = ChatErrorCodes.ChatTurnFailed });
            var (activity, harness) = WithChat();
            harness.StoppedAt(Now.AddSeconds(-1));

            await activity.RunAsync(context: null, Key);

            Assert.Empty(harness.Chat.Sent);
        }

        [Theory]
        [InlineData(true, false, false, SubagentActivityStatus.Completed)]
        [InlineData(false, false, false, SubagentActivityStatus.Failed)]
        [InlineData(false, false, true, SubagentActivityStatus.Cancelled)]
        [InlineData(true, true, true, SubagentActivityStatus.Cancelled)]
        public void FinalStatus_StopWinsOverEveryOtherOutcome(bool success, bool awaitingApproval, bool cancelled, string expected) =>
            Assert.Equal(expected, AgentEventWorkflowActivity.FinalStatus(success, awaitingApproval, cancelled));
    }
}
