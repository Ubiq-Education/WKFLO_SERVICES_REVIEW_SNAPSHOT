using AMAIS.WKFLO.AI.Application.Agents.Actors;
using Dapr.Workflow;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Actors
{
    public class AgentActorGatekeeperTests
    {
        [Fact]
        public void TryEnqueue_NewKey_AddsAndReturnsTrue()
        {
            var queue = new List<string>();

            var added = AgentActorGatekeeper.TryEnqueue(queue, "msg:a");

            Assert.True(added);
            Assert.Equal(["msg:a"], queue);
        }

        [Fact]
        public void TryEnqueue_DuplicateKey_ReturnsFalseWithoutDuplicatingEntry()
        {
            var queue = new List<string> { "msg:a" };

            var added = AgentActorGatekeeper.TryEnqueue(queue, "msg:a");

            Assert.False(added);
            Assert.Equal(["msg:a"], queue);
        }

        [Fact]
        public void TryDequeueNextCandidate_ValidKeyAtHead_ReturnsItWithoutRemovingIt()
        {
            var queue = new List<string> { "msg:a", "msg:b" };

            var found = AgentActorGatekeeper.TryDequeueNextCandidate(queue, out var key, out var instanceId);

            Assert.True(found);
            Assert.Equal("msg:a", key);
            Assert.Equal("a", instanceId);
            // The candidate stays at the head — it's the caller's job to remove it once processed.
            Assert.Equal(["msg:a", "msg:b"], queue);
        }

        [Fact]
        public void TryDequeueNextCandidate_EmptyQueue_ReturnsFalse()
        {
            var queue = new List<string>();

            var found = AgentActorGatekeeper.TryDequeueNextCandidate(queue, out var key, out var instanceId);

            Assert.False(found);
            Assert.Null(key);
            Assert.Null(instanceId);
        }

        [Fact]
        public void TryDequeueNextCandidate_MalformedKeyAtHead_DropsItAndReturnsNextValidKey()
        {
            var queue = new List<string> { "no-prefix", "msg:b" };

            var found = AgentActorGatekeeper.TryDequeueNextCandidate(queue, out var key, out var instanceId);

            Assert.True(found);
            Assert.Equal("msg:b", key);
            Assert.Equal("b", instanceId);
            Assert.Equal(["msg:b"], queue);
        }

        [Fact]
        public void TryDequeueNextCandidate_OnlyMalformedKeys_DropsAllAndReturnsFalse()
        {
            var queue = new List<string> { "no-prefix", "also-bad" };

            var found = AgentActorGatekeeper.TryDequeueNextCandidate(queue, out var key, out var instanceId);

            Assert.False(found);
            Assert.Null(key);
            Assert.Null(instanceId);
            Assert.Empty(queue);
        }

        [Theory]
        [InlineData("msg:instance-1", true, "instance-1")]
        [InlineData("no-prefix", false, null)]
        [InlineData("", false, null)]
        [InlineData(null, false, null)]
        [InlineData("msg:", false, null)]
        public void TryGetInstanceId_ParsesOrRejectsKey(string key, bool expectedSuccess, string expectedInstanceId)
        {
            var success = AgentActorGatekeeper.TryGetInstanceId(key, out var instanceId);

            Assert.Equal(expectedSuccess, success);
            Assert.Equal(expectedInstanceId, instanceId);
        }

        [Theory]
        [InlineData(WorkflowRuntimeStatus.Completed, true)]
        [InlineData(WorkflowRuntimeStatus.Failed, true)]
        [InlineData(WorkflowRuntimeStatus.Terminated, true)]
        [InlineData(WorkflowRuntimeStatus.Running, false)]
        [InlineData(WorkflowRuntimeStatus.Pending, false)]
        [InlineData(WorkflowRuntimeStatus.Suspended, false)]
        public void IsWorkflowFinished_TerminalStatusesReturnTrue_OthersReturnFalse(WorkflowRuntimeStatus status, bool expectedFinished)
        {
            Assert.Equal(expectedFinished, AgentActorGatekeeper.IsWorkflowFinished(status));
        }

        [Fact]
        public void IsWorkflowFinished_NullStatus_ReturnsFalse()
        {
            // A null status means "instance not found yet," which can legitimately happen right after
            // ScheduleNewWorkflowAsync returns, before the engine has persisted the instance — NOT that
            // it's done. AgentActor relies on this to decide whether the head-of-queue candidate still
            // needs scheduling and/or waiting.
            Assert.False(AgentActorGatekeeper.IsWorkflowFinished(null));
        }

        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        [Theory]
        [InlineData("session-1:7", "session-1")]
        [InlineData("a:b:12", "a:b")]
        public void TryGetSessionId_ActorId_ReturnsTheSessionPart(string actorId, string expected)
        {
            Assert.True(AgentActorGatekeeper.TryGetSessionId(actorId, out var sessionId));
            Assert.Equal(expected, sessionId);
            Assert.Equal(actorId, AgentActorGatekeeper.BuildActorId(sessionId, int.Parse(actorId[(actorId.LastIndexOf(':') + 1)..])));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("session-1")]
        [InlineData(":7")]
        [InlineData("session-1:agent")]
        public void TryGetSessionId_NotAnAgentActorId_ReturnsFalse(string actorId)
        {
            Assert.False(AgentActorGatekeeper.TryGetSessionId(actorId, out var sessionId));
            Assert.Null(sessionId);
        }

        [Theory]
        [InlineData(-1, true)]   // queued before the Stop: belongs to the stopped run
        [InlineData(0, false)]   // queued at the very instant: not before it
        [InlineData(1, false)]   // queued after the Stop: the person's next message
        public void IsQueuedBeforeStop_ComparesTheQueueTimeWithTheStop(int secondsFromStop, bool expected) =>
            Assert.Equal(expected, AgentActorGatekeeper.IsQueuedBeforeStop(StoppedAt.AddSeconds(secondsFromStop), StoppedAt));

        [Fact]
        public void IsQueuedBeforeStop_NoStopOrUnknownQueueTime_KeepsTheHandOff()
        {
            Assert.False(AgentActorGatekeeper.IsQueuedBeforeStop(StoppedAt.AddSeconds(-1), null));
            Assert.False(AgentActorGatekeeper.IsQueuedBeforeStop(null, StoppedAt));
        }

        [Fact]
        public void DropQueuedBeforeStop_RemovesOnlyTheHandOffsQueuedBeforeTheStop()
        {
            var queue = new List<string> { "msg:a", "msg:b", "msg:c", "msg:d" };
            var queuedAt = new Dictionary<string, DateTimeOffset?>
            {
                ["msg:a"] = StoppedAt.AddSeconds(-30),
                ["msg:b"] = StoppedAt.AddSeconds(5),   // the person's next message after the Stop
                ["msg:c"] = StoppedAt.AddSeconds(-1),
                ["msg:d"] = null                       // unknown: kept
            };

            var dropped = AgentActorGatekeeper.DropQueuedBeforeStop(queue, queuedAt, StoppedAt);

            Assert.Equal(["msg:a", "msg:c"], dropped);
            Assert.Equal(["msg:b", "msg:d"], queue);
        }

        [Fact]
        public void DropQueuedBeforeStop_NothingQueuedBefore_LeavesTheQueueUntouched()
        {
            var queue = new List<string> { "msg:a" };

            var dropped = AgentActorGatekeeper.DropQueuedBeforeStop(queue, new Dictionary<string, DateTimeOffset?> { ["msg:a"] = StoppedAt.AddSeconds(1) }, StoppedAt);

            Assert.Empty(dropped);
            Assert.Equal(["msg:a"], queue);
        }
    }
}
