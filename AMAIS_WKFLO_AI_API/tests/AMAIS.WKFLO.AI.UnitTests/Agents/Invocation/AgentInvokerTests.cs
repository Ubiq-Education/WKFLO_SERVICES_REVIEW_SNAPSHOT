using AMAIS.WKFLO.AI.Application.Agents.Commands.InvokeAgent;
using AMAIS.WKFLO.AI.Application.Agents.Invocation;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Invocation
{
    public class AgentInvokerTests
    {
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly Mock<IDateTimeProvider> _dateTimeProviderMock;
        private readonly IWkfloMapper _mapper;
        private readonly AgentInvoker _agentInvoker;
        private readonly ChatTurnFailureState _failureState = new();

        private static readonly DateTimeOffset FixedNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

        public AgentInvokerTests()
        {
            _mediatorMock = new Mock<IWkfloMediator>();
            _dateTimeProviderMock = new Mock<IDateTimeProvider>();
            _dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(FixedNow);
            _mapper = WkfloMapperExtensions.CreateMapper();

            _agentInvoker = new AgentInvoker(
                new TenantContext(),
                new ChatContext(),
                new UserContext(),
                new AgentSharableContext(),
                _mapper,
                _mediatorMock.Object,
                _dateTimeProviderMock.Object,
                NullLogger<AgentInvoker>.Instance,
                _failureState);
        }

        private static AgentInvocationRequest BuildRequest(string orchestrationInstanceId = "orch-1", bool? rootIsWidget = null) => new()
        {
            AgentId = 42,
            AgentRole = "reviewer",
            Input = new AgentInvocationInput { Text = "review this" },
            SessionId = "session-1",
            OrchestrationInstanceId = orchestrationInstanceId,
            ChainOfThought = false,
            TenantContext = new TenantContext(),
            ChatContext = new ChatContext(),
            UserContext = new UserContext(),
            AgentSharableContext = new AgentSharableContext(),
            RootIsWidget = rootIsWidget
        };

        [Fact]
        public async Task InvokeAsync_MediatorReturnsSuccess_ReturnsDataWithOrchestrationInstanceIdStamped()
        {
            var expected = new AgentInvocationResult
            {
                AgentId = 42,
                AgentRole = "reviewer",
                Success = true,
                Content = "looks good"
            };

            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SuccessResult<AgentInvocationResult>(expected));

            var result = await _agentInvoker.InvokeAsync(BuildRequest("orch-1"), TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Equal("looks good", result.Content);
            Assert.Equal("orch-1", result.Metadata[nameof(AgentInvocationRequest.OrchestrationInstanceId)]);
        }

        [Fact]
        public async Task InvokeAsync_RequestHasRootIsWidget_ForwardsItOntoInvokeAgentCommand()
        {
            // Both the spin-up (AgentEventWorkflowActivity) and orchestration fan-out
            // (InvokeAgentWorkflowActivity) paths funnel through this single method — RootIsWidget must
            // survive the crossing into InvokeAgentCommand so RunnableAISyncService can resolve this
            // subagent's icon in the root's own context.
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SuccessResult<AgentInvocationResult>(new AgentInvocationResult { AgentId = 42, AgentRole = "reviewer", Success = true }));

            await _agentInvoker.InvokeAsync(BuildRequest(rootIsWidget: false), TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Send(
                It.Is<InvokeAgentCommand>(cmd => cmd.RootIsWidget == false),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task InvokeAsync_NoOrchestrationInstanceId_DoesNotStampMetadata()
        {
            var expected = new AgentInvocationResult { AgentId = 42, AgentRole = "reviewer", Success = true };

            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SuccessResult<AgentInvocationResult>(expected));

            var result = await _agentInvoker.InvokeAsync(BuildRequest(orchestrationInstanceId: null), TestContext.Current.CancellationToken);

            Assert.False(result.Metadata.ContainsKey(nameof(AgentInvocationRequest.OrchestrationInstanceId)));
        }

        [Fact]
        public async Task InvokeAsync_MediatorReturnsNotFound_ReturnsSoftFailureWithAgentNotFoundMessage()
        {
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotFoundResult());

            var result = await _agentInvoker.InvokeAsync(BuildRequest(), TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("Agent not found.", result.ErrorMessage);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, result.ErrorCode);
            Assert.Equal(42, result.AgentId);
            Assert.Equal("reviewer", result.AgentRole);
            Assert.Equal(FixedNow, result.CompletedAtUtc);
        }

        [Fact]
        public async Task InvokeAsync_MediatorReturnsErrorResultWithErrors_ReturnsSoftFailureDescribingErrors()
        {
            var invalidResult = new InvalidResult("Input.Text", "WKFLOAI0002");

            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(invalidResult);

            var result = await _agentInvoker.InvokeAsync(BuildRequest(), TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Contains("WKFLOAI0002", result.ErrorMessage);
            Assert.Contains("Input.Text", result.ErrorMessage);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, result.ErrorCode);
        }

        [Fact]
        public async Task InvokeAsync_MediatorThrows_ReturnsSoftFailureWithACodeAndNoExceptionText()
        {
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SENTINEL model call failed"));

            var result = await _agentInvoker.InvokeAsync(BuildRequest(), TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal(ChatErrorCodes.ChatTurnFailed, result.ErrorCode);
            Assert.DoesNotContain("SENTINEL", result.ErrorMessage);
            Assert.Equal(42, result.AgentId);
            Assert.Equal("reviewer", result.AgentRole);
        }

        [Fact]
        public async Task InvokeAsync_MediatorThrowsATimeout_ClassifiesIt()
        {
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("SENTINEL"));

            var result = await _agentInvoker.InvokeAsync(BuildRequest(), TestContext.Current.CancellationToken);

            Assert.Equal(ChatErrorCodes.ChatModelTimeout, result.ErrorCode);
        }

        [Fact]
        public async Task InvokeAsync_RunRecordedAFailureBeforeThrowing_ReportsWhatTheRunRecorded()
        {
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .Callback(() => _failureState.Record(ChatErrorCodes.ChatModelQuotaExceeded, agentId: 42))
                .ThrowsAsync(new InvalidOperationException("history save failed"));

            var result = await _agentInvoker.InvokeAsync(BuildRequest(), TestContext.Current.CancellationToken);

            Assert.Equal(ChatErrorCodes.ChatModelQuotaExceeded, result.ErrorCode);
        }

        [Fact]
        public async Task InvokeAsync_NotFoundAfterARecordedFailure_IsNotReportedAsAgentUnavailable()
        {
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .Callback(() => _failureState.Record(ChatErrorCodes.ChatModelAuthFailed, agentId: 42))
                .ReturnsAsync(new NotFoundResult());

            var result = await _agentInvoker.InvokeAsync(BuildRequest(), TestContext.Current.CancellationToken);

            Assert.Equal(ChatErrorCodes.ChatModelAuthFailed, result.ErrorCode);
        }

        [Fact]
        public async Task InvokeAsync_StoppedWhileRunning_RethrowsTheCancellationInsteadOfASoftFailure()
        {
            // US 7501: the caller must tell a Stop apart from a failure. A provider may wrap the cancellation.
            using var stop = new CancellationTokenSource();
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .Callback(() => stop.Cancel())
                .ThrowsAsync(new InvalidOperationException("stream aborted"));

            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _agentInvoker.InvokeAsync(BuildRequest(), stop.Token));

            Assert.Equal(stop.Token, ex.CancellationToken);
        }

        [Fact]
        public async Task InvokeAsync_CompletedJustBeforeTheStop_IsReportedAsCompleted()
        {
            // US 7501 (review fix): the hand-off finished successfully, then the Stop landed: it stays completed.
            using var stop = new CancellationTokenSource();
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .Callback(() => stop.Cancel())
                .ReturnsAsync(new SuccessResult<AgentInvocationResult>(new AgentInvocationResult { AgentId = 42, Success = true, Content = "done" }));

            var result = await _agentInvoker.InvokeAsync(BuildRequest(), stop.Token);

            Assert.True(result.Success);
            Assert.Equal("done", result.Content);
        }

        [Fact]
        public async Task InvokeAsync_FailedResultOnceStopped_IsReportedAsTheStop()
        {
            // US 7501: a handler that swallows the cancellation and returns a failure must not turn the Stop into a failure.
            using var stop = new CancellationTokenSource();
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<InvokeAgentCommand>(), It.IsAny<CancellationToken>()))
                .Callback(() => stop.Cancel())
                .ReturnsAsync(new NotFoundResult());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _agentInvoker.InvokeAsync(BuildRequest(), stop.Token));
        }

        [Fact]
        public async Task InvokeAsync_NullRequest_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _agentInvoker.InvokeAsync(null, TestContext.Current.CancellationToken));
        }
    }
}
