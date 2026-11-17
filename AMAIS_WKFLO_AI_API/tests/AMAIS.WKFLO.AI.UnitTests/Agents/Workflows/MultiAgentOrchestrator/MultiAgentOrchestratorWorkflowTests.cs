using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Workflows.MultiAgentOrchestrator;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using Dapr.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Workflows.MultiAgentOrchestrator
{
    /// <summary>US 7501: a Stop ends the orchestration - no retry of the stopped step and no fan-out after it.</summary>
    public class MultiAgentOrchestratorWorkflowTests
    {
        private static readonly DateTime StartedAtUtc = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<WorkflowContext> _contextMock = new();
        private readonly List<AgentInvocationRequest> _requests = [];

        public MultiAgentOrchestratorWorkflowTests()
        {
            _contextMock.SetupGet(x => x.InstanceId).Returns("orch-1");
            _contextMock.SetupGet(x => x.CurrentUtcDateTime).Returns(StartedAtUtc);
            _contextMock.Setup(x => x.CreateReplaySafeLogger<MultiAgentOrchestratorWorkflow>()).Returns(NullLogger<MultiAgentOrchestratorWorkflow>.Instance);
        }

        private static MultiAgentOrchestratorWorkflowInput BuildInput() => new()
        {
            SessionId = "session-1",
            Input = new AgentInvocationInput { Text = "review" },
            LeadAgent = new() { AgentId = 1, AgentRole = "lead" },
            FanOutAgents = [new() { AgentId = 2, AgentRole = "critic" }, new() { AgentId = 3, AgentRole = "editor" }]
        };

        private void SetupActivity(Func<AgentInvocationRequest, AgentInvocationResult> resultFor) =>
            _contextMock
                .Setup(x => x.CallActivityAsync<AgentInvocationResult>(nameof(InvokeAgentWorkflowActivity), It.IsAny<object>(), It.IsAny<WorkflowTaskOptions>()))
                .Returns<string, object, WorkflowTaskOptions>((_, request, _) =>
                {
                    var invocation = (AgentInvocationRequest)request;
                    _requests.Add(invocation);
                    return Task.FromResult(resultFor(invocation));
                });

        [Fact]
        public async Task RunAsync_LeadStepStopped_DoesNotStartTheFanOut()
        {
            SetupActivity(r => new AgentInvocationResult { AgentId = r.AgentId, AgentRole = r.AgentRole, Success = false, Cancelled = true });

            var output = await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            Assert.Single(_requests);
            Assert.True(output.LeadResult.Cancelled);
            Assert.Empty(output.FanOutResults);
            Assert.False(output.Success);
        }

        [Fact]
        public async Task RunAsync_StampsEveryStepWithTheOrchestrationStart()
        {
            SetupActivity(r => new AgentInvocationResult { AgentId = r.AgentId, AgentRole = r.AgentRole, Success = true, Content = "ok" });

            var output = await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            Assert.Equal(3, _requests.Count);
            Assert.All(_requests, r => Assert.Equal(new DateTimeOffset(StartedAtUtc, TimeSpan.Zero), r.RequestedAt));
            Assert.True(output.Success);
            Assert.Empty(_notifications);
        }

        // --- US 7959 / Task 7963: what the chat is told when agents fail ---

        private readonly List<NotifyChatErrorRequest> _notifications = [];

        private void SetupNotify() =>
            _contextMock
                .Setup(x => x.CallActivityAsync<bool>(nameof(NotifyChatErrorWorkflowActivity), It.IsAny<object>(), It.IsAny<WorkflowTaskOptions>()))
                .Returns<string, object, WorkflowTaskOptions>((_, request, options) =>
                {
                    Assert.Null(options?.RetryPolicy); // reported once, never retried
                    _notifications.Add((NotifyChatErrorRequest)request);
                    return Task.FromResult(true);
                });

        private static AgentInvocationResult Ok(AgentInvocationRequest r) =>
            new() { AgentId = r.AgentId, AgentRole = r.AgentRole, Success = true, Content = "ok" };

        private static AgentInvocationResult Failed(AgentInvocationRequest r, string code) =>
            new() { AgentId = r.AgentId, AgentRole = r.AgentRole, Success = false, ErrorCode = code, ErrorMessage = "SENTINEL" };

        [Fact]
        public async Task RunAsync_OneFanOutAgentFails_ReportsOnlyThatAgent_NonTerminal()
        {
            SetupNotify();
            SetupActivity(r => r.AgentId == 3 ? Failed(r, ChatErrorCodes.ChatModelRateLimited) : Ok(r));

            await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            var notification = Assert.Single(_notifications);
            Assert.False(notification.Failure.IsTerminal);
            Assert.True(notification.SendFailedActivity);
            Assert.Equal(3, notification.Failure.AgentId);
            Assert.Equal("editor", notification.Failure.AgentName);
            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, notification.Failure.ErrorCode);
            Assert.Equal("orch-1", notification.Failure.TurnId);
            Assert.Equal("orch-1", notification.OrchestrationInstanceId);
            Assert.Equal("orch-1:agent:3", notification.Failure.DedupeKey);
            Assert.Equal("session-1", notification.Failure.SessionId);
        }

        [Fact]
        public async Task RunAsync_LeadFailsButAFanOutAgentAnswers_ReportsTheLeadOnly()
        {
            SetupNotify();
            SetupActivity(r => r.AgentId == 1 ? Failed(r, ChatErrorCodes.ChatModelTimeout) : Ok(r));

            await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            var notification = Assert.Single(_notifications);
            Assert.Equal(1, notification.Failure.AgentId);
            Assert.False(notification.Failure.IsTerminal);
        }

        [Fact]
        public async Task RunAsync_EveryAgentFailsTheSameWay_ReportsEachAgentOnceThenOneTerminalErrorWithThatCode()
        {
            SetupNotify();
            SetupActivity(r => Failed(r, ChatErrorCodes.ChatModelQuotaExceeded));

            await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            Assert.Equal(4, _notifications.Count);
            Assert.Equal([1, 2, 3], _notifications.Take(3).Select(n => n.Failure.AgentId.Value).Order());
            Assert.All(_notifications.Take(3), n => Assert.False(n.Failure.IsTerminal));

            var terminal = _notifications[3];
            Assert.True(terminal.Failure.IsTerminal);
            Assert.Null(terminal.Failure.AgentId);
            Assert.False(terminal.SendFailedActivity);
            Assert.Equal(ChatErrorCodes.ChatModelQuotaExceeded, terminal.Failure.ErrorCode);
            Assert.Equal([1, 2, 3], terminal.Failure.TypingAgents.Select(a => a.AgentId).Order());
            Assert.Null(terminal.Failure.DedupeKey); // one terminal error per turn (the notifier's default key)
        }

        [Fact]
        public async Task RunAsync_EveryAgentFailsDifferently_TerminalErrorIsSubagentFailed()
        {
            SetupNotify();
            SetupActivity(r => Failed(r, r.AgentId == 1 ? ChatErrorCodes.ChatModelTimeout : ChatErrorCodes.ChatModelUnavailable));

            await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            Assert.Equal(ChatErrorCodes.ChatSubagentFailed, _notifications.Single(n => n.Failure.IsTerminal).Failure.ErrorCode);
        }

        [Fact]
        public async Task RunAsync_OnlyAgentDeliveredItsOutputWithABlankAnswer_ReportsNothing()
        {
            // PR 16990 review: e.g. an image-generation agent posted its image (InvokeAgentCommandHandler returns a success
            // with blank content once the answer was delivered) - no failed card, no marker, no terminal error.
            SetupNotify();
            SetupActivity(r => new AgentInvocationResult { AgentId = r.AgentId, AgentRole = r.AgentRole, Success = true, Content = string.Empty });
            var input = BuildInput();
            input.FanOutAgents = [];

            var output = await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, input);

            Assert.True(output.Success);
            Assert.Empty(_notifications);
        }

        [Fact]
        public async Task RunAsync_LeadStopped_ReportsNothing()
        {
            SetupNotify();
            SetupActivity(r => new AgentInvocationResult { AgentId = r.AgentId, AgentRole = r.AgentRole, Success = false, Cancelled = true });

            await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            Assert.Empty(_notifications);
        }

        [Fact]
        public async Task RunAsync_FanOutStoppedAfterTheLeadFailed_ReportsTheLeadButNoTerminalError()
        {
            SetupNotify();
            SetupActivity(r => r.AgentId == 1
                ? Failed(r, ChatErrorCodes.ChatModelTimeout)
                : new AgentInvocationResult { AgentId = r.AgentId, AgentRole = r.AgentRole, Success = false, Cancelled = true });

            await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            var notification = Assert.Single(_notifications);
            Assert.Equal(1, notification.Failure.AgentId);
            Assert.False(notification.Failure.IsTerminal);
        }

        [Fact]
        public async Task RunAsync_NoChatSession_ReportsNothing()
        {
            SetupNotify();
            SetupActivity(r => Failed(r, ChatErrorCodes.ChatTurnFailed));
            var input = BuildInput();
            input.SessionId = null;

            await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, input);

            Assert.Empty(_notifications);
        }

        [Fact]
        public async Task RunAsync_AgentFailsAfterExhaustingItsRetries_IsReportedOnceWithAClassifiedCodeAndNoExceptionText()
        {
            SetupNotify();
            _contextMock
                .Setup(x => x.CallActivityAsync<AgentInvocationResult>(nameof(InvokeAgentWorkflowActivity), It.IsAny<object>(), It.IsAny<WorkflowTaskOptions>()))
                .Returns<string, object, WorkflowTaskOptions>((_, request, _) =>
                {
                    var invocation = (AgentInvocationRequest)request;
                    if (invocation.AgentId == 2)
                        throw new WorkflowTaskFailedException("SENTINEL activity failed",
                            new WorkflowTaskFailureDetails(typeof(TimeoutException).FullName, "SENTINEL the call timed out", "at Foo.Bar()"));
                    return Task.FromResult(Ok(invocation));
                });

            var output = await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            var critic = output.FanOutResults.Single(r => r.AgentId == 2);
            Assert.Equal(ChatErrorCodes.ChatModelTimeout, critic.ErrorCode);
            Assert.DoesNotContain("SENTINEL", critic.ErrorMessage);

            var notification = Assert.Single(_notifications);
            Assert.Equal(2, notification.Failure.AgentId);
            Assert.Equal(ChatErrorCodes.ChatModelTimeout, notification.Failure.ErrorCode);
        }

        [Fact]
        public async Task RunAsync_ReportingFails_DoesNotFailTheOrchestration()
        {
            _contextMock
                .Setup(x => x.CallActivityAsync<bool>(nameof(NotifyChatErrorWorkflowActivity), It.IsAny<object>(), It.IsAny<WorkflowTaskOptions>()))
                .ThrowsAsync(new WorkflowTaskFailedException("boom", new WorkflowTaskFailureDetails("System.Exception", "boom", null)));
            SetupActivity(r => Failed(r, ChatErrorCodes.ChatTurnFailed));

            var output = await new MultiAgentOrchestratorWorkflow().RunAsync(_contextMock.Object, BuildInput());

            Assert.False(output.Success);
        }
    }
}
