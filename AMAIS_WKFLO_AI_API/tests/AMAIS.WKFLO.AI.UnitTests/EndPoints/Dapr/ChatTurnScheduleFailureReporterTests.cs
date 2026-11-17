using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.Dapr;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using Dapr.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.EndPoints.Dapr
{
    /// <summary>
    /// US 7959 / Task 7961: when a chat message's workflow cannot be scheduled, the chat is told (best effort) and the
    /// queued message is not left behind - unless the workflow exists after all.
    /// </summary>
    public class ChatTurnScheduleFailureReporterTests
    {
        private const string Key = "msg:wf-9";
        private const string InstanceId = "wf-9";
        private static readonly DateTimeOffset QueuedAt = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

        private readonly Mock<DaprClient> _dapr = new();
        private readonly Mock<IChatTurnErrorNotifier> _notifier = new();
        private readonly List<ChatTurnErrorNotification> _notifications = [];

        public ChatTurnScheduleFailureReporterTests()
        {
            _notifier.Setup(x => x.NotifyAsync(It.IsAny<ChatTurnErrorNotification>(), It.IsAny<CancellationToken>()))
                .Callback((ChatTurnErrorNotification n, CancellationToken _) => _notifications.Add(n))
                .ReturnsAsync(true);
        }

        private void Queued(string sessionId) =>
            _dapr.Setup(x => x.GetStateAsync<WkfloQueueMessage<VoiceMessagePublicAgentByShareIdCommand>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, Key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WkfloQueueMessage<VoiceMessagePublicAgentByShareIdCommand>
                {
                    Command = new VoiceMessagePublicAgentByShareIdCommand { SessionId = sessionId },
                    TenantContext = new TenantContext { TenantName = "tenant-a" },
                    QueuedAt = QueuedAt
                });

        private Task ReportAsync(Func<Task<bool>> workflowExists) =>
            ChatTurnScheduleFailureReporter.ReportAsync<VoiceMessagePublicAgentByShareIdCommand>(Key, InstanceId, workflowExists,
                _dapr.Object, _notifier.Object, NullLogger.Instance);

        private void VerifyDeleted(Func<Times> times) =>
            _dapr.Verify(x => x.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, Key,
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), times);

        [Fact]
        public async Task WorkflowNotScheduled_TellsTheChat_AndDeletesTheQueuedMessage()
        {
            Queued("session-9");

            await ReportAsync(() => Task.FromResult(false));

            var notification = Assert.Single(_notifications);
            Assert.Equal("session-9", notification.SessionId);
            Assert.Equal("tenant-a", notification.TenantName);
            Assert.Equal(InstanceId, notification.TurnId);
            Assert.Equal(QueuedAt, notification.TurnStartedAt);
            Assert.Equal(ChatErrorCodes.ChatInfrastructureError, notification.Failure.Code);
            Assert.True(notification.Failure.IsTerminal);
            VerifyDeleted(Times.Once);
        }

        [Fact]
        public async Task WorkflowExistsAfterAll_DoesNothing()
        {
            Queued("session-9");

            await ReportAsync(() => Task.FromResult(true));

            Assert.Empty(_notifications);
            VerifyDeleted(Times.Never);
        }

        [Fact]
        public async Task NoReadableSession_StillDeletesTheQueuedMessage()
        {
            Queued(sessionId: null);

            await ReportAsync(() => Task.FromResult(false));

            Assert.Empty(_notifications);
            VerifyDeleted(Times.Once);
        }

        [Fact]
        public async Task WorkflowCheckFails_TellsTheChat_ButKeepsTheQueuedMessage()
        {
            Queued("session-9");

            await ReportAsync(() => throw new InvalidOperationException("sidecar down"));

            Assert.Single(_notifications);
            VerifyDeleted(Times.Never);
        }

        [Fact]
        public async Task MalformedKey_DoesNothing_AndNeverThrows()
        {
            await ChatTurnScheduleFailureReporter.ReportAsync<VoiceMessagePublicAgentByShareIdCommand>("x", instanceId: null,
                () => Task.FromResult(false), _dapr.Object, _notifier.Object, NullLogger.Instance);

            Assert.Empty(_notifications);
            VerifyDeleted(Times.Never);
        }

        // --- US 7959 / Task 7963: the multi-agent orchestrator's subscriber already read its queued request ---

        [Fact]
        public async Task OrchestratorNotScheduled_TellsTheChatOnceWithATerminalInfrastructureError()
        {
            await ChatTurnScheduleFailureReporter.ReportAsync(InstanceId, "tenant-a", "session-9", QueuedAt, () => Task.FromResult(false),
                _notifier.Object, NullLogger.Instance);

            var notification = Assert.Single(_notifications);
            Assert.Equal("session-9", notification.SessionId);
            Assert.Equal("tenant-a", notification.TenantName);
            Assert.Equal(InstanceId, notification.TurnId);
            Assert.Equal(QueuedAt, notification.TurnStartedAt);
            Assert.Equal(ChatErrorCodes.ChatInfrastructureError, notification.Failure.Code);
            Assert.True(notification.Failure.IsTerminal);
            VerifyDeleted(Times.Never); // the subscriber drops its queued request itself
        }

        [Fact]
        public async Task OrchestratorAlreadyScheduled_TellsTheChatNothing()
        {
            await ChatTurnScheduleFailureReporter.ReportAsync(InstanceId, "tenant-a", "session-9", QueuedAt, () => Task.FromResult(true),
                _notifier.Object, NullLogger.Instance);

            Assert.Empty(_notifications);
        }

        [Fact]
        public async Task OrchestratorWithoutASession_TellsTheChatNothing()
        {
            await ChatTurnScheduleFailureReporter.ReportAsync(InstanceId, "tenant-a", sessionId: null, QueuedAt, () => Task.FromResult(false),
                _notifier.Object, NullLogger.Instance);

            Assert.Empty(_notifications);
        }
    }
}
