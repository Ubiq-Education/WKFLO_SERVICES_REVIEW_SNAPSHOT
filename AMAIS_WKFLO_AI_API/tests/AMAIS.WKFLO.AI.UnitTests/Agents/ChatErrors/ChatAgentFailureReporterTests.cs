using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using static AMAIS.WKFLO.Common.Models.CommonConsts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.ChatErrors
{
    /// <summary>US 7959 / Task 7963: one agent's failure (or the whole run's) reaches the chat once, as codes only.</summary>
    public class ChatAgentFailureReporterTests
    {
        private static readonly DateTimeOffset StartedAt = ChatErrorHarness.Now.AddSeconds(-30);

        private readonly ChatErrorHarness _harness = new();

        private static ChatAgentFailure AgentFailure(bool isTerminal = false, bool ensureIdle = false) => new()
        {
            TenantName = "tenant-a",
            SessionId = "session-1",
            TurnId = "orch-1",
            TurnStartedAt = StartedAt,
            ErrorCode = ChatErrorCodes.ChatModelRateLimited,
            AgentId = isTerminal ? null : 7,
            AgentName = isTerminal ? null : "critic",
            IsTerminal = isTerminal,
            EnsureIdle = ensureIdle,
            TypingAgents = isTerminal ? [new() { AgentId = 1, AgentName = "lead" }, new() { AgentId = 7, AgentName = "critic" }] : []
        };

        [Fact]
        public async Task ReportAsync_AgentFailure_StopsThatAgentsBubbleAndSendsANonTerminalMarker()
        {
            var notified = await _harness.Reporter.ReportAsync(AgentFailure(), TestContext.Current.CancellationToken);

            Assert.True(notified);
            Assert.Equal([false], _harness.Chat.TypingOf(7));
            var marker = Assert.Single(_harness.Chat.Markers());
            Assert.Equal(7, marker.AgentId);
            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, marker.Message.Metadata[CommonMetadataConsts.ErrorCode]);
            Assert.Equal(false, marker.Message.Metadata[CommonMetadataConsts.IsTerminal]);
            Assert.Empty(_harness.Chat.States());
        }

        [Fact]
        public async Task ReportAsync_SameAgentTwice_IsReportedOnce()
        {
            Assert.True(await _harness.Reporter.ReportAsync(AgentFailure(), TestContext.Current.CancellationToken));
            Assert.False(await _harness.Reporter.ReportAsync(AgentFailure(), TestContext.Current.CancellationToken));

            Assert.Single(_harness.Chat.Markers());
        }

        [Fact]
        public async Task ReportAsync_Terminal_StopsEveryBubbleAndEndsTheRunWithError()
        {
            await _harness.Reporter.ReportAsync(AgentFailure(isTerminal: true), TestContext.Current.CancellationToken);

            Assert.Equal([false], _harness.Chat.TypingOf(1));
            Assert.Equal([false], _harness.Chat.TypingOf(7));
            var marker = Assert.Single(_harness.Chat.Markers());
            Assert.Equal(true, marker.Message.Metadata[CommonMetadataConsts.IsTerminal]);
            Assert.Equal(["error"], _harness.Chat.States());
        }

        [Fact]
        public async Task ReportAsync_EnsureIdle_LeavesProcessingAfterTheMarker()
        {
            await _harness.Reporter.ReportAsync(AgentFailure(ensureIdle: true), TestContext.Current.CancellationToken);

            Assert.Equal(["idle"], _harness.Chat.States());
        }

        [Fact]
        public async Task ReportAsync_EnsureIdle_NotWhenANewerTurnOfTheSessionRuns()
        {
            using var newer = _harness.TurnRegistry.Register("tenant-a", "session-1", StartedAt.AddSeconds(5));

            await _harness.Reporter.ReportAsync(AgentFailure(ensureIdle: true), TestContext.Current.CancellationToken);

            Assert.Single(_harness.Chat.Markers());
            Assert.Empty(_harness.Chat.States());
        }

        [Fact]
        public async Task ReportAsync_StoppedByTheUser_SendsNothing()
        {
            _harness.StoppedAt(StartedAt.AddSeconds(1));

            Assert.False(await _harness.Reporter.ReportAsync(AgentFailure(ensureIdle: true), TestContext.Current.CancellationToken));
            Assert.False(await _harness.Reporter.ReportAsync(new ChatAgentFailure { SessionId = "session-1", StopRequested = true, ErrorCode = ChatErrorCodes.ChatTurnFailed }, TestContext.Current.CancellationToken));

            Assert.Empty(_harness.Chat.Sent);
        }

        [Fact]
        public async Task ReportAsync_UnknownCode_IsSentAsTheGenericCode()
        {
            var failure = AgentFailure();
            failure.ErrorCode = "SENTINEL boom at Foo.Bar()";

            await _harness.Reporter.ReportAsync(failure, TestContext.Current.CancellationToken);

            Assert.Equal(ChatErrorCodes.ChatTurnFailed, _harness.Chat.Markers().Single().Message.Metadata[CommonMetadataConsts.ErrorCode]);
            _harness.Chat.AssertNoLeak("SENTINEL", "Foo.Bar");
        }
    }
}
