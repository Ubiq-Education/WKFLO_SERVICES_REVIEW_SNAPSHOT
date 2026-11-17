using AMAIS.WKFLO.AI.Application.Agents.Invocation;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Workflows.MultiAgentOrchestrator;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.SignalR;
using Dapr.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Workflows.MultiAgentOrchestrator
{
    public class InvokeAgentWorkflowActivityTests
    {
        private readonly Mock<IAgentInvoker> _agentInvokerMock;
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly InvokeAgentWorkflowActivity _activity;
        private readonly WorkflowActivityContext _context;
        private readonly ChatTurnRegistry _turnRegistry = new();
        private readonly Mock<IChatStopCheckpoint> _stopCheckpointMock = new();

        private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        public InvokeAgentWorkflowActivityTests()
        {
            _agentInvokerMock = new Mock<IAgentInvoker>();

            _dbMock = new Mock<IMasterDataDbContext>();
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            _notificationServiceMock = new Mock<IChatNotificationService>();
            _notificationServiceMock
                .Setup(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            _activity = new InvokeAgentWorkflowActivity(_agentInvokerMock.Object, _dbMock.Object, _notificationServiceMock.Object, NullLogger<InvokeAgentWorkflowActivity>.Instance,
                _turnRegistry, _stopCheckpointMock.Object, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now));

            // WorkflowActivityContext is abstract with no public implementation available outside the
            // Dapr Workflow SDK's own engine — Moq can proxy an abstract class directly.
            var contextMock = new Mock<WorkflowActivityContext>();
            contextMock.SetupGet(x => x.TaskExecutionKey).Returns("task-exec-1");
            contextMock.SetupGet(x => x.InstanceId).Returns("orch-1");
            _context = contextMock.Object;
        }

        private static AgentInvocationRequest BuildInput() => new()
        {
            AgentId = 7,
            AgentRole = "critic",
            Input = new AgentInvocationInput { Text = "review" },
            SessionId = "session-1",
            OrchestrationInstanceId = "orch-1",
            TenantContext = new AMAIS.WKFLO.Common.Contexts.TenantContext { TenantName = "tenant-a" }
        };

        [Fact]
        public async Task RunAsync_CallsAgentInvokerDirectly_NoActorProxyInvolved()
        {
            var input = BuildInput();
            var expected = new AgentInvocationResult { AgentId = 7, AgentRole = "critic", Success = true, Content = "ok" };

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            var result = await _activity.RunAsync(_context, input);

            Assert.Same(expected, result);
            _agentInvokerMock.Verify(x => x.InvokeAsync(input, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunAsync_RequestHasRootIsWidget_ForwardsUnchangedToAgentInvoker()
        {
            // This is a fan-out agent's receiving side of a multi-agent orchestration run: a fresh DI
            // scope with no memory of the lead agent's own context, so RootIsWidget (set by
            // MultiAgentOrchestratorWorkflow from the lead's resolved context — see
            // TriggerMultiAgentOrchestratorCommandHandler) can only reach IAgentInvoker via this
            // activity forwarding the request unchanged.
            var input = BuildInput();
            input.RootIsWidget = false;

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "critic", Success = true, Content = "ok" });

            await _activity.RunAsync(_context, input);

            _agentInvokerMock.Verify(x => x.InvokeAsync(
                It.Is<AgentInvocationRequest>(r => r.RootIsWidget == false),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunAsync_AgentInvokerThrows_RethrowsForDaprRetryPolicy()
        {
            var input = BuildInput();

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("sidecar unreachable"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _activity.RunAsync(_context, input));
        }

        [Fact]
        public async Task RunAsync_Invoked_SendsStartedUpdateBeforeInvokingAgent()
        {
            var input = BuildInput();

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "critic", Success = true, Content = "ok" });

            await _activity.RunAsync(_context, input);

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                "session-1", 7, "critic", "started", It.IsAny<string>(), It.IsAny<string>(), "orch-1", It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_AgentInvokerSucceeds_SendsCompletedUpdate()
        {
            var input = BuildInput();

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "critic", Success = true, Content = "ok" });

            await _activity.RunAsync(_context, input);

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                "session-1", 7, "critic", "completed", It.IsAny<string>(), It.IsAny<string>(), "orch-1", null),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_AgentInvokerReturnsFailure_ReturnsItWithoutSendingAFailedUpdate()
        {
            // US 7959 / Task 7963: the workflow reports the final failure once (NotifyChatErrorWorkflowActivity), with a code.
            var input = BuildInput();

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "critic", Success = false, ErrorCode = "WKFLOAI1103", ErrorMessage = "Agent not found." });

            var result = await _activity.RunAsync(_context, input);

            Assert.Equal("WKFLOAI1103", result.ErrorCode);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), SubagentActivityStatus.Failed, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityFailedUpdate(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_AgentInvokerThrows_RethrowsWithoutSendingAFailedUpdate()
        {
            // US 7959 / Task 7963: this attempt may still be retried - no "failed" card per attempt, and never ex.Message.
            var input = BuildInput();

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SENTINEL sidecar unreachable"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _activity.RunAsync(_context, input));

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), SubagentActivityStatus.Failed, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(e => e != null)),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_NotificationServiceThrows_DoesNotBreakAgentInvocation()
        {
            var input = BuildInput();
            var expected = new AgentInvocationResult { AgentId = 7, AgentRole = "critic", Success = true, Content = "ok" };

            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            _notificationServiceMock
                .Setup(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("hub unavailable"));

            var result = await _activity.RunAsync(_context, input);

            Assert.Same(expected, result);
        }

        private void SetupInvokerStoppedMidRun() =>
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .Returns<AgentInvocationRequest, CancellationToken>((_, ct) =>
                {
                    _turnRegistry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddSeconds(1));
                    return Task.FromException<AgentInvocationResult>(new OperationCanceledException(ct));
                });

        [Fact]
        public async Task RunAsync_StoppedByTheUser_ReturnsACancelledResultInsteadOfThrowing()
        {
            // US 7501 (approved change 4): returning, not throwing, is what keeps Dapr's retry policy from running it again.
            SetupInvokerStoppedMidRun();

            var result = await _activity.RunAsync(_context, BuildInput());

            Assert.True(result.Cancelled);
            Assert.False(result.Success);
            Assert.Equal(7, result.AgentId);
            Assert.Equal("critic", result.AgentRole);
        }

        [Fact]
        public async Task RunAsync_StoppedByTheUser_SendsCancelledInsteadOfFailed()
        {
            SetupInvokerStoppedMidRun();

            await _activity.RunAsync(_context, BuildInput());

            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                "session-1", 7, "critic", SubagentActivityStatus.Cancelled, It.IsAny<string>(), It.IsAny<string>(), "orch-1", null),
                Times.Once);
            _notificationServiceMock.Verify(x => x.SendSubagentActivityUpdate(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), SubagentActivityStatus.Failed, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_RegistersTheRunFromTheOrchestrationStart_AndPassesItsTokenToTheInvoker()
        {
            // US 7501: a step that only starts after the Stop still belongs to an orchestration requested before it.
            var input = BuildInput();
            input.RequestedAt = Now.AddMinutes(-2);

            var cancelledByStop = 0;
            CancellationToken invokerToken = default;
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AgentInvocationRequest, CancellationToken>((_, ct) =>
                {
                    invokerToken = ct;
                    cancelledByStop = _turnRegistry.CancelTurnsStartedBefore("tenant-a", "session-1", Now.AddMinutes(-1));
                })
                .ReturnsAsync(new AgentInvocationResult { AgentId = 7, AgentRole = "critic", Success = true });

            await _activity.RunAsync(_context, input);

            Assert.Equal(1, cancelledByStop);
            Assert.True(invokerToken.IsCancellationRequested);
            _stopCheckpointMock.Verify(x => x.ThrowIfStoppedAsync("tenant-a", "session-1", ChatStopCheckpoints.AgentInvocationStart, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunAsync_CancellationNotCausedByAStop_StillRethrowsForDaprRetryPolicy()
        {
            // Unchanged behaviour: only a Stop skips the retry policy.
            _agentInvokerMock
                .Setup(x => x.InvokeAsync(It.IsAny<AgentInvocationRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("timeout"));

            await Assert.ThrowsAsync<OperationCanceledException>(() => _activity.RunAsync(_context, BuildInput()));
        }
    }
}
