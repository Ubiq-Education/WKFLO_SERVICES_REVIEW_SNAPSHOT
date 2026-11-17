using AMAIS.WKFLO.AI.Application.Agents.Commands.InvokeAgent;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Results;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.InvokeAgent
{
    /// <summary>
    /// US 7959 / Task 7963: an agent run of a multi-agent orchestration or a hand-off only succeeds with an answer; a failed
    /// run carries the code the run recorded, and a run that failed is never reported as "agent not found".
    /// </summary>
    public class InvokeAgentCommandHandlerTests
    {
        private readonly Mock<IRunnableAISyncService> _runnable = new();
        private readonly ChatContext _chatContext = new();
        private readonly ChatTurnFailureState _failureState = new();
        private readonly InvokeAgentCommandHandler _handler;

        public InvokeAgentCommandHandlerTests()
        {
            _handler = new InvokeAgentCommandHandler(_runnable.Object, Mock.Of<IDateTimeProvider>(), _chatContext, _failureState);
        }

        private static InvokeAgentCommand Command() => new()
        {
            Id = 7,
            AgentRole = "critic",
            SessionId = "session-1",
            Input = new AgentInvocationInput { Text = "review" }
        };

        private void RunReturns(string message, System.Action duringRun = null) =>
            _runnable.Setup(x => x.RunRunnableAgentByIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(),
                    It.IsAny<bool>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .Callback(() => duringRun?.Invoke())
                .ReturnsAsync(message == null ? null : new RunnableAgentData { Message = message });

        private async Task<AgentInvocationResult> Handle()
        {
            var result = await _handler.Handle(Command(), TestContext.Current.CancellationToken);
            return Assert.IsType<SuccessResult<AgentInvocationResult>>(result).Data;
        }

        [Fact]
        public async Task Handle_Answer_IsASuccess()
        {
            RunReturns("looks good");

            var result = await Handle();

            Assert.True(result.Success);
            Assert.Equal("looks good", result.Content);
            Assert.Null(result.ErrorCode);
        }

        [Fact]
        public async Task Handle_RunFailedBeforeStreaming_IsAFailureWithTheRecordedCode_NotAgentNotFound()
        {
            RunReturns(null, () => _failureState.Record(ChatErrorCodes.ChatModelRateLimited, agentId: 7));

            var result = await Handle();

            Assert.False(result.Success);
            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, result.ErrorCode);
        }

        [Fact]
        public async Task Handle_StreamFailedButTheRunReturned_IsAFailure()
        {
            RunReturns("partial answ", () => _failureState.Record(ChatErrorCodes.ChatModelUnavailable, agentId: 7));

            var result = await Handle();

            Assert.False(result.Success);
            Assert.Equal(ChatErrorCodes.ChatModelUnavailable, result.ErrorCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Handle_BlankAnswer_IsNotASuccess(string answer)
        {
            RunReturns(answer);

            var result = await Handle();

            Assert.False(result.Success);
            Assert.Equal(ChatErrorCodes.ChatModelEmptyResponse, result.ErrorCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Handle_BlankAnswerAfterItsOutputWasDelivered_IsASuccess(string answer)
        {
            // PR 16990 review: e.g. an image-generation agent posts its image and answers with no text.
            RunReturns(answer, () => _failureState.MarkAnswerDelivered());

            var result = await Handle();

            Assert.True(result.Success);
            Assert.Null(result.ErrorCode);
        }

        [Fact]
        public async Task Handle_BlankAnswerWhileAToolApprovalIsPending_IsASuccessAwaitingApproval()
        {
            RunReturns(string.Empty, () => _chatContext.ToolApprovalRequestedThisTurn = true);

            var result = await Handle();

            Assert.True(result.Success);
            Assert.True(result.AwaitingApproval);
        }

        [Fact]
        public async Task Handle_ToolWarningOnly_DoesNotFailTheRun()
        {
            RunReturns("looks good", () => _failureState.Record(ChatErrorCodes.ChatToolFailed, agentId: 7));

            var result = await Handle();

            Assert.True(result.Success);
        }

        [Fact]
        public async Task Handle_AgentNotFound_IsNotFound()
        {
            RunReturns(null);

            var result = await _handler.Handle(Command(), TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
        }
    }
}
