using AMAIS.WKFLO.AI.UnitTests.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatStop
{
    /// <summary>US 7501: the per-replica turn registry and what a replica does with a received Stop broadcast.</summary>
    public class ChatTurnRegistryTests
    {
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly ChatTurnRegistry _registry = new();

        [Fact]
        public void CancelTurnsStartedBefore_CancelsOnlyTheSessionsTurnsStartedBeforeTheStop()
        {
            using var before = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-5));
            using var after = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(1));
            using var otherSession = _registry.Register("tenant-a", "session-2", StoppedAt.AddSeconds(-5));

            var cancelled = _registry.CancelTurnsStartedBefore("tenant-a", "session-1", StoppedAt);

            Assert.Equal(1, cancelled);
            Assert.True(before.Token.IsCancellationRequested);
            Assert.True(before.StopRequested);
            Assert.False(after.Token.IsCancellationRequested);
            Assert.False(after.StopRequested);
            Assert.False(otherSession.Token.IsCancellationRequested);
        }

        [Fact]
        public void CancelTurnsStartedBefore_SecondStopDoesNotCountTheSameTurnTwice()
        {
            using var turn = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-5));

            Assert.Equal(1, _registry.CancelTurnsStartedBefore("tenant-a", "session-1", StoppedAt));
            Assert.Equal(0, _registry.CancelTurnsStartedBefore("tenant-a", "session-1", StoppedAt.AddSeconds(1)));
        }

        [Fact]
        public void Dispose_UnregistersTheTurn_SoALaterStopFindsNothing()
        {
            var turn = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-5));
            Assert.Equal(1, _registry.CountFor("tenant-a", "session-1"));

            turn.Dispose();

            Assert.Equal(0, _registry.CountFor("tenant-a", "session-1"));
            Assert.Equal(0, _registry.CancelTurnsStartedBefore("tenant-a", "session-1", StoppedAt));
        }

        [Fact]
        public void Dispose_KeepsTheOtherTurnsOfTheSession()
        {
            var first = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-5));
            using var second = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-4));

            first.Dispose();

            Assert.Equal(1, _registry.CountFor("tenant-a", "session-1"));
            Assert.Equal(1, _registry.CancelTurnsStartedBefore("tenant-a", "session-1", StoppedAt));
            Assert.True(second.StopRequested);
        }

        [Fact]
        public void HasRunningTurnStartedAfter_OnlyCountsNewerTurnsNotStopped_OfTheSameTenantAndSession()
        {
            using var stopped = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-5));
            _registry.CancelTurnsStartedBefore("tenant-a", "session-1", StoppedAt);
            using var otherTenant = _registry.Register("tenant-b", "session-1", StoppedAt.AddSeconds(2));

            Assert.False(_registry.HasRunningTurnStartedAfter("tenant-a", "session-1", stopped.StartedAt));

            using var newer = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(1));

            Assert.True(_registry.HasRunningTurnStartedAfter("tenant-a", "session-1", stopped.StartedAt));
            Assert.False(_registry.HasRunningTurnStartedAfter("tenant-a", "session-1", newer.StartedAt));
        }

        [Fact]
        public async Task ConcurrentRegisterAndDispose_LeavesNoTurnsBehind()
        {
            await Task.WhenAll(Enumerable(200, i => Task.Run(() =>
            {
                using var turn = _registry.Register("tenant-a", "session-1", StoppedAt);
            })));

            Assert.Equal(0, _registry.CountFor("tenant-a", "session-1"));
        }

        [Fact]
        public void BroadcastHandler_CancelsOnlyTurnsStartedBeforeTheBroadcastStop()
        {
            var handler = new ChatStopBroadcastHandler(_registry, NullLogger<ChatStopBroadcastHandler>.Instance);
            using var before = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-5));
            using var after = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(5));

            var cancelled = handler.Handle(new ChatStopSignal("tenant-a", "session-1", StoppedAt).ToJson());

            Assert.Equal(1, cancelled);
            Assert.True(before.StopRequested);
            Assert.False(after.StopRequested);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"sessionId\":\"\",\"stoppedAt\":\"2026-10-19T12:00:00+00:00\"}")]
        [InlineData("{\"sessionId\":\"session-1\"}")]
        public void BroadcastHandler_IgnoresMalformedPayloads(string payload)
        {
            var handler = new ChatStopBroadcastHandler(_registry, NullLogger<ChatStopBroadcastHandler>.Instance);
            using var turn = _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-5));

            Assert.Equal(0, handler.Handle(payload));
            Assert.False(turn.StopRequested);
        }

        [Theory]
        [InlineData(true, LogLevel.Information)]
        [InlineData(false, LogLevel.Debug)]
        public void BroadcastHandler_LogsTheReceivedStop_AtInformationOnlyWhenItCancelsATurn(bool hasRunningTurn, LogLevel expectedLevel)
        {
            var logger = new RecordingLogger<ChatStopBroadcastHandler>();
            var handler = new ChatStopBroadcastHandler(_registry, logger);
            using var turn = hasRunningTurn ? _registry.Register("tenant-a", "session-1", StoppedAt.AddSeconds(-5)) : null;

            handler.Handle(new ChatStopSignal("tenant-a", "session-1", StoppedAt));

            var entry = Assert.Single(logger.With("CancelledTurns"));
            Assert.Equal(expectedLevel, entry.Level);
            Assert.Equal(hasRunningTurn ? 1 : 0, entry.Values["CancelledTurns"]);
            Assert.Equal("session-1", entry.Values["SessionId"]);
        }

        [Fact]
        public void Signal_RoundTripsThroughJson()
        {
            var json = new ChatStopSignal("tenant-a", "session-1", StoppedAt).ToJson();

            Assert.True(ChatStopSignal.TryParse(json, out var parsed));
            Assert.Equal("session-1", parsed.SessionId);
            Assert.Equal(StoppedAt, parsed.StoppedAt);
        }

        private static Task[] Enumerable(int count, Func<int, Task> factory)
        {
            var tasks = new Task[count];
            for (var i = 0; i < count; i++)
                tasks[i] = factory(i);
            return tasks;
        }
    }
}
