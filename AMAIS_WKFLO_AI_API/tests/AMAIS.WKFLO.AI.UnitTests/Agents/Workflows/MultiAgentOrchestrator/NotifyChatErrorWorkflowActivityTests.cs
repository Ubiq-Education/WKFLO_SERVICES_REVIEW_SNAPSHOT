using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.AI.Application.Agents.Workflows.MultiAgentOrchestrator;
using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.SignalR;
using Dapr.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Workflows.MultiAgentOrchestrator
{
    /// <summary>
    /// US 7959 / Task 7963: a failed orchestrator agent reaches the chat once - its failed card with a code, its bubble
    /// stopped, a non-terminal marker - and a run nobody answered ends with one terminal error. Never after a Stop, never
    /// with exception text.
    /// </summary>
    public class NotifyChatErrorWorkflowActivityTests
    {
        private static readonly DateTimeOffset StartedAt = ChatErrorHarness.Now.AddMinutes(-1);

        private readonly ChatErrorHarness _harness = new();
        private readonly NotifyChatErrorWorkflowActivity _activity;
        private readonly WorkflowActivityContext _context = new Mock<WorkflowActivityContext>().Object;

        public NotifyChatErrorWorkflowActivityTests()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            _activity = new NotifyChatErrorWorkflowActivity(_harness.Reporter, _harness.Chat.Object, db.Object, NullLogger<NotifyChatErrorWorkflowActivity>.Instance);
        }

        private static NotifyChatErrorRequest AgentFailed(int agentId, string code = ChatErrorCodes.ChatModelRateLimited) => new()
        {
            Failure = new ChatAgentFailure
            {
                TenantName = "tenant-a",
                SessionId = "session-1",
                TurnId = "orch-1",
                TurnStartedAt = StartedAt,
                ErrorCode = code,
                AgentId = agentId,
                AgentName = "critic",
                DedupeKey = $"orch-1:agent:{agentId}"
            },
            SendFailedActivity = true,
            OrchestrationInstanceId = "orch-1"
        };

        private static NotifyChatErrorRequest RunFailed() => new()
        {
            Failure = new ChatAgentFailure
            {
                TenantName = "tenant-a",
                SessionId = "session-1",
                TurnId = "orch-1",
                TurnStartedAt = StartedAt,
                ErrorCode = ChatErrorCodes.ChatSubagentFailed,
                IsTerminal = true,
                TypingAgents = [new() { AgentId = 1, AgentName = "lead" }, new() { AgentId = 7, AgentName = "critic" }]
            },
            OrchestrationInstanceId = "orch-1"
        };

        [Fact]
        public async Task RunAsync_AgentFailed_SendsOneFailedCardWithTheCode_TypingOff_AndANonTerminalMarker()
        {
            Assert.True(await _activity.RunAsync(_context, AgentFailed(7)));

            var card = Assert.Single(_harness.Chat.SubagentUpdatesOf(7));
            Assert.Equal(SubagentActivityStatus.Failed, card.Status);
            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, card.ErrorCode);
            Assert.Null(card.Error);
            Assert.Equal("orch-1", card.OrchestrationInstanceId);
            Assert.Equal([false], _harness.Chat.TypingOf(7));
            var marker = Assert.Single(_harness.Chat.Markers());
            Assert.Equal(false, marker.Message.Metadata[CommonMetadataConsts.IsTerminal]);
            Assert.Empty(_harness.Chat.States());
        }

        [Fact]
        public async Task RunAsync_SameAgentReportedAgain_SendsNothingMore()
        {
            await _activity.RunAsync(_context, AgentFailed(7));
            Assert.False(await _activity.RunAsync(_context, AgentFailed(7)));

            Assert.Single(_harness.Chat.SubagentUpdatesOf(7));
            Assert.Single(_harness.Chat.Markers());
        }

        [Fact]
        public async Task RunAsync_RunFailed_StopsEveryBubbleAndEndsTheTurnWithOneError()
        {
            await _activity.RunAsync(_context, AgentFailed(1));
            await _activity.RunAsync(_context, AgentFailed(7));
            await _activity.RunAsync(_context, RunFailed());
            await _activity.RunAsync(_context, RunFailed());

            var terminal = Assert.Single(_harness.Chat.Markers(), m => (bool)m.Message.Metadata[CommonMetadataConsts.IsTerminal]);
            Assert.Equal(ChatErrorCodes.ChatSubagentFailed, terminal.Message.Metadata[CommonMetadataConsts.ErrorCode]);
            Assert.Equal(["error"], _harness.Chat.States());
            Assert.All(new[] { 1, 7 }, id => Assert.DoesNotContain(true, _harness.Chat.TypingOf(id)));
            Assert.Equal(2, _harness.Chat.Sent.Count(s => s.Method == HubReceiverMethods.SubagentActivityUpdate));
        }

        [Fact]
        public async Task RunAsync_StoppedByTheUser_SendsNothing()
        {
            _harness.StoppedAt(StartedAt.AddSeconds(10));

            Assert.False(await _activity.RunAsync(_context, AgentFailed(7)));
            Assert.False(await _activity.RunAsync(_context, RunFailed()));

            Assert.Empty(_harness.Chat.Sent);
        }

        [Fact]
        public async Task RunAsync_ToolCodeForAFailedAgent_IsSentAsAnError()
        {
            await _activity.RunAsync(_context, AgentFailed(7, ChatErrorCodes.ChatToolUnavailable));

            Assert.Equal(ChatErrorCodes.ChatTurnFailed, _harness.Chat.SubagentUpdatesOf(7).Single().ErrorCode);
            Assert.Equal(ChatErrorCodes.ChatTurnFailed, _harness.Chat.Markers().Single().Message.Metadata[CommonMetadataConsts.ErrorCode]);
        }

        [Fact]
        public async Task RunAsync_NeverSendsFreeText()
        {
            await _activity.RunAsync(_context, AgentFailed(7, "SENTINEL System.InvalidOperationException: boom at Foo.Bar()"));
            await _activity.RunAsync(_context, RunFailed());

            _harness.Chat.AssertNoLeak("SENTINEL", "InvalidOperationException", "Foo.Bar");
        }
    }
}
