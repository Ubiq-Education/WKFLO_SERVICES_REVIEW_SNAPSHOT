using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors
{
    /// <summary>US 7959: the turn's failure state keeps the first terminal failure.</summary>
    public class ChatTurnFailureStateTests
    {
        private readonly ChatTurnFailureState _state = new();

        [Fact]
        public void NewState_HasNoFailure()
        {
            Assert.False(_state.HasFailure);
            Assert.Null(_state.Failure);
            Assert.Null(_state.Exception);
        }

        [Fact]
        public void Record_FirstTerminalFailureWins()
        {
            var first = new TimeoutException();

            Assert.True(_state.Record(ChatErrorCodes.ChatModelTimeout, agentId: 1, exception: first));
            Assert.False(_state.Record(ChatErrorCodes.ChatModelUnavailable, agentId: 2));

            Assert.True(_state.HasFailure);
            Assert.Equal(ChatErrorCodes.ChatModelTimeout, _state.Failure.Code);
            Assert.Equal(1, _state.Failure.AgentId);
            Assert.Same(first, _state.Exception);
        }

        [Fact]
        public void Record_TerminalFailureReplacesAWarning()
        {
            Assert.True(_state.Record(ChatErrorCodes.ChatToolUnavailable));
            Assert.True(_state.Record(ChatErrorCodes.ChatModelRateLimited));

            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, _state.Failure.Code);
        }

        [Fact]
        public void Record_WarningNeverReplacesAnything()
        {
            Assert.True(_state.Record(ChatErrorCodes.ChatToolUnavailable));
            Assert.False(_state.Record(ChatErrorCodes.ChatToolFailed));

            Assert.Equal(ChatErrorCodes.ChatToolUnavailable, _state.Failure.Code);
        }

        [Fact]
        public void Record_Null_IsIgnored()
        {
            Assert.False(_state.Record((ChatTurnFailure)null));
            Assert.False(_state.HasFailure);
        }

        /// <summary>Task 7961: the answer-delivered flag is independent of what was recorded and only ever turns on.</summary>
        [Fact]
        public void MarkAnswerDelivered_SetsTheFlag_WithoutRecordingAFailure()
        {
            Assert.False(_state.AnswerDelivered);

            _state.MarkAnswerDelivered();
            _state.MarkAnswerDelivered();

            Assert.True(_state.AnswerDelivered);
            Assert.False(_state.HasFailure);
        }

        [Fact]
        public async Task Record_ConcurrentTerminalFailures_ExactlyOneWins()
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 64)
                .Select(i => Task.Run(() => _state.Record(ChatErrorCodes.ChatModelUnavailable, agentId: i), TestContext.Current.CancellationToken)));

            Assert.Equal(1, results.Count(r => r));
        }
    }
}
