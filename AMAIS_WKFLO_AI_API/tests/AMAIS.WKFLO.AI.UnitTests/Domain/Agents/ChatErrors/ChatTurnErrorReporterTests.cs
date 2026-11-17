using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959 / Task 7961: tool warnings of the running turn go to the chat live, once per tool per turn, as non-terminal
    /// warnings of the answering agent; outside a registered turn they are only recorded.
    /// </summary>
    public class ChatTurnErrorReporterTests
    {
        private static readonly DateTimeOffset StartedAt = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

        private readonly Mock<IChatTurnErrorNotifier> _notifier = new();
        private readonly ChatTurnFailureState _state = new();
        private readonly List<ChatTurnErrorNotification> _notifications = [];
        private readonly ChatTurnErrorReporter _reporter;
        private bool _stopRequested;

        public ChatTurnErrorReporterTests()
        {
            _notifier.Setup(x => x.NotifyAsync(It.IsAny<ChatTurnErrorNotification>(), It.IsAny<CancellationToken>()))
                .Callback((ChatTurnErrorNotification n, CancellationToken _) => _notifications.Add(n))
                .ReturnsAsync(true);
            _reporter = new ChatTurnErrorReporter(_notifier.Object, _state, NullLogger<ChatTurnErrorReporter>.Instance);
        }

        private void Begin() => _reporter.BeginTurn(new ChatTurnErrorTurn
        {
            TenantName = "tenant-a",
            SessionId = "session-1",
            TurnId = "wf-1",
            TurnStartedAt = StartedAt,
            IsStopRequested = () => _stopRequested
        });

        [Fact]
        public async Task ReportWarning_InATurn_SendsANonTerminalWarningOfTheAnsweringAgent()
        {
            Begin();
            _reporter.SetCurrentAgent(new ChatTurnAgent(5, "Main", "https://icons/main.png"));

            var sent = await _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolUnavailable, "plugin_12_MCP");

            Assert.True(sent);
            var notification = Assert.Single(_notifications);
            Assert.Equal("tenant-a", notification.TenantName);
            Assert.Equal("session-1", notification.SessionId);
            Assert.Equal("wf-1", notification.TurnId);
            Assert.Equal(StartedAt, notification.TurnStartedAt);
            Assert.False(notification.StopRequested);
            Assert.Equal(ChatErrorCodes.ChatToolUnavailable, notification.Failure.Code);
            Assert.Equal(ChatErrorSeverity.Warning, notification.Failure.Severity);
            Assert.False(notification.Failure.IsTerminal);
            Assert.Equal(5, notification.Failure.AgentId);
            Assert.Equal("https://icons/main.png", notification.Failure.ChatbotIconUrl);
            Assert.Contains("plugin_12_MCP", notification.DedupeKey);

            // A warning is reported live, so it is not left for the turn's choke point.
            Assert.False(_state.HasFailure);
        }

        [Fact]
        public async Task ReportWarning_SameToolTwice_IsSentOnce_AnotherToolIsSentToo()
        {
            Begin();
            _reporter.SetCurrentAgent(new ChatTurnAgent(5, "Main"));

            await _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolFailed, "plugin_1_HTTP");
            await _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolFailed, "plugin_1_HTTP");
            await _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolFailed, "plugin_2_MCP");

            Assert.Equal(2, _notifications.Count);
            Assert.NotEqual(_notifications[0].DedupeKey, _notifications[1].DedupeKey);
        }

        [Fact]
        public async Task ReportWarning_PassesTheTurnsStopFlag()
        {
            Begin();
            _stopRequested = true;

            await _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolFailed, "plugin_1_HTTP");

            Assert.True(Assert.Single(_notifications).StopRequested);
        }

        [Fact]
        public async Task ReportWarning_ExplicitAgentOtherThanTheCurrentOne_UsesThatAgent()
        {
            Begin();
            _reporter.SetCurrentAgent(new ChatTurnAgent(5, "Main"));

            await _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolUnavailable, "plugin_3_MCP", agentId: 9, agentName: "Other");

            var failure = Assert.Single(_notifications).Failure;
            Assert.Equal(9, failure.AgentId);
            Assert.Equal("Other", failure.AgentName);
        }

        [Fact]
        public async Task ReportWarning_OutsideATurn_IsOnlyRecorded()
        {
            var sent = await _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolUnavailable, "plugin_12_MCP", agentId: 5);

            Assert.False(sent);
            Assert.Empty(_notifications);
            Assert.Equal(ChatErrorCodes.ChatToolUnavailable, _state.Failure?.Code);
        }

        [Fact]
        public async Task ReportWarning_WithAnErrorCode_IsRecordedForTheChokePoint_NotSentLive()
        {
            Begin();

            var sent = await _reporter.ReportWarningAsync(ChatErrorCodes.ChatModelUnavailable, "plugin_12_MCP", agentId: 5);

            Assert.False(sent);
            Assert.Empty(_notifications);
            Assert.Equal(ChatErrorCodes.ChatModelUnavailable, _state.Failure?.Code);
        }

        [Fact]
        public async Task ReportWarning_NotifierThrows_NeverThrows()
        {
            Begin();
            _notifier.Setup(x => x.NotifyAsync(It.IsAny<ChatTurnErrorNotification>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var sent = await _reporter.ReportWarningAsync(ChatErrorCodes.ChatToolFailed, "plugin_1_HTTP", agentId: 5);

            Assert.False(sent);
        }
    }
}
