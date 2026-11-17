using AMAIS.WKFLO.AI.UnitTests.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatStop
{
    /// <summary>US 7501: the single stop-state read a turn makes at its checkpoints (turn start, before each tool call).</summary>
    public class ChatStopCheckpointTests
    {
        private static readonly DateTimeOffset TurnStart = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<IChatStopStateStore> _stateStore = new();
        private readonly ChatTurnRegistry _registry = new();
        private readonly ChatStopCheckpoint _checkpoint;

        public ChatStopCheckpointTests()
        {
            _checkpoint = new ChatStopCheckpoint(_stateStore.Object, _registry, NullLogger<ChatStopCheckpoint>.Instance);
        }

        private static AutoFunctionInvocationContext AutoFunctionContext(CancellationToken token)
        {
            var function = KernelFunctionFactory.CreateFromMethod(() => "ran", "some_tool");
            return new AutoFunctionInvocationContext(new Kernel(), function, new FunctionResult(function), new ChatHistory(), new ChatMessageContent(AuthorRole.Assistant, ""))
            {
                CancellationToken = token
            };
        }

        private void StoppedAt(DateTimeOffset? stoppedAt) =>
            _stateStore.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ReturnsAsync(stoppedAt);

        [Fact]
        public async Task StoppedAfterTheTurnStarted_CancelsTheTurnAsAStop()
        {
            StoppedAt(TurnStart.AddSeconds(3));
            using var turn = _registry.Register("tenant-a", "session-1", TurnStart);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _checkpoint.ThrowIfStoppedAsync("tenant-a", "session-1", ChatStopCheckpoints.TurnStart, turn.Token));

            Assert.True(turn.StopRequested);
            _stateStore.Verify(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(-3)]
        [InlineData(null)]
        public async Task StoppedBeforeTheTurnStartedOrNeverStopped_LetsTheTurnRun(int? stoppedSecondsFromStart)
        {
            StoppedAt(stoppedSecondsFromStart.HasValue ? TurnStart.AddSeconds(stoppedSecondsFromStart.Value) : null);
            using var turn = _registry.Register("tenant-a", "session-1", TurnStart);

            await _checkpoint.ThrowIfStoppedAsync("tenant-a", "session-1", ChatStopCheckpoints.TurnStart, turn.Token);

            Assert.False(turn.StopRequested);
        }

        [Theory]
        [InlineData(3, 1)]
        [InlineData(-3, 0)]
        public async Task LogsTheCheckpoint_OnlyWhenItCancelsATurn(int stoppedSecondsFromStart, int expectedLogs)
        {
            var logger = new RecordingLogger<ChatStopCheckpoint>();
            var checkpoint = new ChatStopCheckpoint(_stateStore.Object, _registry, logger);
            StoppedAt(TurnStart.AddSeconds(stoppedSecondsFromStart));
            using var turn = _registry.Register("tenant-a", "session-1", TurnStart);

            try
            {
                await checkpoint.ThrowIfStoppedAsync("tenant-a", "session-1", ChatStopCheckpoints.ToolCall, turn.Token);
            }
            catch (OperationCanceledException)
            {
                // The stopped case; what matters here is the log.
            }

            var logs = logger.With("Checkpoint");
            Assert.Equal(expectedLogs, logs.Count);
            if (expectedLogs > 0)
                Assert.Equal(ChatStopCheckpoints.ToolCall, logs[0].Values["Checkpoint"]);
        }

        [Fact]
        public async Task ReadFailure_NeverFailsTheTurn()
        {
            _stateStore.Setup(x => x.GetStoppedAtAsync("tenant-a", "session-1", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("state store down"));
            using var turn = _registry.Register("tenant-a", "session-1", TurnStart);

            await _checkpoint.ThrowIfStoppedAsync("tenant-a", "session-1", ChatStopCheckpoints.TurnStart, turn.Token);

            Assert.False(turn.StopRequested);
        }

        [Fact]
        public async Task AutoFunctionFilter_StoppedSession_DoesNotRunTheTool()
        {
            StoppedAt(TurnStart.AddSeconds(3));
            using var turn = _registry.Register("tenant-a", "session-1", TurnStart);
            var filter = new ChatStopCheckpointFilter(_checkpoint, "tenant-a", "session-1");
            var context = AutoFunctionContext(turn.Token);
            var toolRan = false;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => filter.OnAutoFunctionInvocationAsync(context, _ =>
            {
                toolRan = true;
                return Task.CompletedTask;
            }));

            Assert.False(toolRan);
            Assert.True(turn.StopRequested);
        }

        [Fact]
        public async Task AutoFunctionFilter_NotStopped_RunsTheTool()
        {
            StoppedAt(null);
            using var turn = _registry.Register("tenant-a", "session-1", TurnStart);
            var filter = new ChatStopCheckpointFilter(_checkpoint, "tenant-a", "session-1");
            var context = AutoFunctionContext(turn.Token);
            var toolRan = false;

            await filter.OnAutoFunctionInvocationAsync(context, _ =>
            {
                toolRan = true;
                return Task.CompletedTask;
            });

            Assert.True(toolRan);
        }
    }
}
