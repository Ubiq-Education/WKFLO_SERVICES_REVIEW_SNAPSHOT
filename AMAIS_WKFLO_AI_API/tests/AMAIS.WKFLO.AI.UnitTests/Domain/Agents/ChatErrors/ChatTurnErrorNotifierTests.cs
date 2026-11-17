using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Dapr;
using Dapr.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959: the notifier stops the thinking bubbles, sends the live marker (and the "error" state when terminal), once
    /// per turn, never after a user Stop, and never with exception text.
    /// </summary>
    public class ChatTurnErrorNotifierTests
    {
        private const string Tenant = "Tenant-A";
        private const string Session = "session-1";
        private const string Turn = "turn-1";
        private const string Sentinel = "SENTINEL-secret-from-exception";
        private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset TurnStartedAt = Now.AddSeconds(-20);

        private readonly Mock<IChatNotificationService> _notifications = new();
        private readonly Mock<IChatStopStateStore> _stopState = new();
        private readonly Mock<DaprClient> _dapr = new();
        private readonly Mock<IDateTimeProvider> _clock = new();
        private readonly List<(string Method, object Payload)> _sent = [];
        private readonly ChatTurnErrorNotifier _notifier;

        public ChatTurnErrorNotifierTests()
        {
            _clock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            _notifications.Setup(x => x.SendTypingIndicator(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string chatId, bool isTyping, int? agentId, string agentName, string icon) =>
                    _sent.Add((HubReceiverMethods.TypingIndicator, new TypingIndicatorUpdate { ChatId = chatId, IsTyping = isTyping, AgentId = agentId, AgentName = agentName, ChatbotIconUrl = icon })))
                .Returns(Task.CompletedTask);
            _notifications.Setup(x => x.SendMessageToChat(It.IsAny<string>(), It.IsAny<ChatMessage>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string chatId, ChatMessage message, int? agentId, string agentName, string icon) =>
                    _sent.Add((HubReceiverMethods.ReceiveMessage, new MessageUpdate { ChatId = chatId, Message = message, AgentId = agentId, AgentName = agentName, ChatbotIconUrl = icon })))
                .Returns(Task.CompletedTask);
            _notifications.Setup(x => x.SendConversationStateUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Callback((string chatId, string state, Dictionary<string, object> metadata) =>
                    _sent.Add((HubReceiverMethods.ConversationStateUpdate, new ConversationStateUpdate { ChatId = chatId, State = state, Metadata = metadata })))
                .Returns(Task.CompletedTask);

            ClaimResult(true);

            _notifier = new ChatTurnErrorNotifier(_notifications.Object, _stopState.Object, _dapr.Object, _clock.Object, NullLogger<ChatTurnErrorNotifier>.Instance);
        }

        private void ClaimResult(bool saved) =>
            _dapr.Setup(x => x.TrySaveStateAsync(CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<ChatTurnErrorClaim>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(saved);

        private static ChatTurnErrorNotification Notification(ChatTurnFailure failure, IReadOnlyList<ChatTurnAgent> typingAgents = null, bool stopRequested = false) => new()
        {
            TenantName = Tenant,
            SessionId = Session,
            TurnId = Turn,
            TurnStartedAt = TurnStartedAt,
            StopRequested = stopRequested,
            Failure = failure,
            TypingAgents = typingAgents ?? []
        };

        private static ChatTurnFailure Terminal(int agentId = 5) =>
            ChatTurnFailure.Create(ChatErrorCodes.ChatModelRateLimited, agentId, "Main agent", "https://icons/main.png");

        [Fact]
        public async Task Notify_TerminalFailure_SendsTypingOffThenMarkerThenErrorState()
        {
            var sent = await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken);

            Assert.True(sent);
            Assert.Equal([HubReceiverMethods.TypingIndicator, HubReceiverMethods.ReceiveMessage, HubReceiverMethods.ConversationStateUpdate],
                _sent.Select(s => s.Method));

            var typing = Assert.IsType<TypingIndicatorUpdate>(_sent[0].Payload);
            Assert.False(typing.IsTyping);
            Assert.Equal(5, typing.AgentId);

            var message = Assert.IsType<MessageUpdate>(_sent[1].Payload);
            Assert.Equal(5, message.AgentId);
            Assert.Equal("Main agent", message.AgentName);
            Assert.Equal(FailedTurnMarker.BuildMessageId(Turn, Terminal()), message.Message.Id);
            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, message.Message.Metadata[CommonMetadataConsts.ErrorCode]);
            Assert.Equal(true, message.Message.Metadata[CommonMetadataConsts.IsTerminal]);

            var state = Assert.IsType<ConversationStateUpdate>(_sent[2].Payload);
            Assert.Equal("error", state.State);
            Assert.Equal(2, state.Metadata.Count);
            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, state.Metadata[CommonMetadataConsts.ErrorCode]);
            Assert.Equal(Turn, state.Metadata[CommonMetadataConsts.TurnId]);
        }

        [Fact]
        public async Task Notify_Warning_StopsOnlyTheFailingAgentAndKeepsTheTurnGoing()
        {
            var warning = ChatTurnFailure.Create(ChatErrorCodes.ChatToolUnavailable, 7, "Tool agent");

            var sent = await _notifier.NotifyAsync(Notification(warning, [new ChatTurnAgent(5, "Main agent")]), TestContext.Current.CancellationToken);

            Assert.True(sent);
            Assert.Equal([HubReceiverMethods.TypingIndicator, HubReceiverMethods.ReceiveMessage], _sent.Select(s => s.Method));
            Assert.Equal(7, Assert.IsType<TypingIndicatorUpdate>(_sent[0].Payload).AgentId);
            var marker = Assert.IsType<MessageUpdate>(_sent[1].Payload).Message;
            Assert.Equal(FailedTurnMarker.ToolWarningText, marker.Content);
            Assert.Equal(ChatErrorSeverity.Warning, marker.Metadata[CommonMetadataConsts.Severity]);
            _notifications.Verify(x => x.SendConversationStateUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
        }

        [Fact]
        public async Task Notify_TerminalFailure_StopsEveryKnownAgentOnce()
        {
            await _notifier.NotifyAsync(Notification(Terminal(5), [new ChatTurnAgent(5, "Main agent"), new ChatTurnAgent(8, "Researcher"), new ChatTurnAgent(9, "Writer")]),
                TestContext.Current.CancellationToken);

            var typing = _sent.Where(s => s.Method == HubReceiverMethods.TypingIndicator).Select(s => (TypingIndicatorUpdate)s.Payload).ToList();
            Assert.Equal([5, 8, 9], typing.Select(t => t.AgentId.Value));
            Assert.All(typing, t => Assert.False(t.IsTyping));
        }

        [Fact]
        public async Task Notify_AlreadyReported_SendsNothing()
        {
            ClaimResult(false);

            var sent = await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken);

            Assert.False(sent);
            Assert.Empty(_sent);
        }

        [Fact]
        public async Task Notify_ClaimsTheDedupeKeyInsertOnlyWithATtl()
        {
            await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.TrySaveStateAsync(CommonAiPubSubConsts.StateStoreName, "chat-error-tenant-a:session-1:turn-1",
                It.Is<ChatTurnErrorClaim>(c => c.ErrorCode == ChatErrorCodes.ChatModelRateLimited), string.Empty,
                It.Is<StateOptions>(o => o.Concurrency == ConcurrencyMode.FirstWrite),
                It.Is<IReadOnlyDictionary<string, string>>(m => m["ttlInSeconds"] == ChatTurnErrorConsts.StateTtlSeconds.ToString()),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Notify_WarningsAreDedupedPerAgentAndCode()
        {
            await _notifier.NotifyAsync(Notification(ChatTurnFailure.Create(ChatErrorCodes.ChatToolFailed, 7)), TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.TrySaveStateAsync(CommonAiPubSubConsts.StateStoreName, $"chat-error-tenant-a:session-1:turn-1:7:{ChatErrorCodes.ChatToolFailed}",
                It.IsAny<ChatTurnErrorClaim>(), It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Notify_InsertConflictReportedAsDaprError_IsADuplicate()
        {
            _dapr.Setup(x => x.TrySaveStateAsync(CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<ChatTurnErrorClaim>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DaprException("possible etag mismatch"));
            _dapr.Setup(x => x.GetStateAsync<ChatTurnErrorClaim>(CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), ConsistencyMode.Strong,
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatTurnErrorClaim { ErrorCode = ChatErrorCodes.ChatModelRateLimited });

            Assert.False(await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken));
            Assert.Empty(_sent);
        }

        [Fact]
        public async Task Notify_StateStoreDown_StillReportsTheError()
        {
            _dapr.Setup(x => x.TrySaveStateAsync(CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<ChatTurnErrorClaim>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DaprException("state store unavailable"));
            _dapr.Setup(x => x.GetStateAsync<ChatTurnErrorClaim>(CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), ConsistencyMode.Strong,
                    It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DaprException("state store unavailable"));

            Assert.True(await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken));
            Assert.Equal(3, _sent.Count);
        }

        [Fact]
        public async Task Notify_TurnStopFlagSet_SendsNothing()
        {
            Assert.False(await _notifier.NotifyAsync(Notification(Terminal(), stopRequested: true), TestContext.Current.CancellationToken));
            Assert.Empty(_sent);
            _dapr.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Notify_StoppedAfterTheTurnStarted_SendsNothing()
        {
            _stopState.Setup(x => x.GetStoppedAtAsync(Tenant, Session, It.IsAny<CancellationToken>())).ReturnsAsync(TurnStartedAt.AddSeconds(5));

            Assert.False(await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken));
            Assert.Empty(_sent);
        }

        [Fact]
        public async Task Notify_StoppedBeforeTheTurnStarted_StillReports()
        {
            // An earlier Stop of the same session: this turn is the user's next message.
            _stopState.Setup(x => x.GetStoppedAtAsync(Tenant, Session, It.IsAny<CancellationToken>())).ReturnsAsync(TurnStartedAt.AddSeconds(-5));

            Assert.True(await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Notify_StopStateUnreadable_StillReports()
        {
            _stopState.Setup(x => x.GetStoppedAtAsync(Tenant, Session, It.IsAny<CancellationToken>())).ThrowsAsync(new DaprException("down"));

            Assert.True(await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Notify_ASendFails_TheOthersAreStillSentAndNothingThrows()
        {
            _notifications.Setup(x => x.SendTypingIndicator(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("hub down"));

            Assert.True(await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken));
            Assert.Equal([HubReceiverMethods.ReceiveMessage, HubReceiverMethods.ConversationStateUpdate], _sent.Select(s => s.Method));
        }

        [Fact]
        public async Task Notify_UnexpectedFailure_NeverThrows()
        {
            _clock.Setup(x => x.DateTimeOffsetUtcNow()).Throws(new InvalidOperationException("clock broken"));

            Assert.False(await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Notify_InsertConflict_RereadsTheKeyWithStrongConsistency()
        {
            _dapr.Setup(x => x.TrySaveStateAsync(CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<ChatTurnErrorClaim>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DaprException("possible etag mismatch"));

            await _notifier.NotifyAsync(Notification(Terminal()), TestContext.Current.CancellationToken);

            _dapr.Verify(x => x.GetStateAsync<ChatTurnErrorClaim>(CommonAiPubSubConsts.StateStoreName, "chat-error-tenant-a:session-1:turn-1",
                ConsistencyMode.Strong, It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Notify_NoTenant_SendsNothingAndTouchesNoState(string tenantName)
        {
            Assert.False(await _notifier.NotifyAsync(Notification(Terminal()) with { TenantName = tenantName }, TestContext.Current.CancellationToken));
            Assert.Empty(_sent);
            _dapr.VerifyNoOtherCalls();
            _stopState.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Notify_NoSession_SendsNothing(string sessionId)
        {
            Assert.False(await _notifier.NotifyAsync(Notification(Terminal()) with { SessionId = sessionId }, TestContext.Current.CancellationToken));
            Assert.Empty(_sent);
        }

        [Fact]
        public async Task Notify_NoExceptionTextEverReachesThePayloads()
        {
            // The failure comes from a real exception full of internal text, recorded the way the turn will record it.
            var exception = new HttpOperationException(HttpStatusCode.InternalServerError, $"{{\"error\":\"{Sentinel}\"}}", Sentinel,
                new InvalidOperationException(Sentinel));
            var state = new ChatTurnFailureState();
            state.Record(ChatErrorClassifier.Classify(exception, stopRequested: false), agentId: 5, agentName: "Main agent", exception: exception);

            await _notifier.NotifyAsync(Notification(state.Failure, [new ChatTurnAgent(8, "Researcher")]), TestContext.Current.CancellationToken);

            Assert.Equal(4, _sent.Count);
            foreach (var (method, payload) in _sent)
                Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(payload, payload.GetType()));
            Assert.Equal(ChatErrorCodes.ChatModelUnavailable,
                Assert.IsType<MessageUpdate>(_sent.Single(s => s.Method == HubReceiverMethods.ReceiveMessage).Payload).Message.Metadata[CommonMetadataConsts.ErrorCode]);
        }
    }
}
