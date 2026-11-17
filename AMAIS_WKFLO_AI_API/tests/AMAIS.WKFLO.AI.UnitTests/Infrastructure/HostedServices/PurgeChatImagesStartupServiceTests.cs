using AMAIS.WKFLO.AI.WebAPI.Infrastructure.HostedServices;
using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.HostedServices
{
    /// <summary>
    /// Exercises <see cref="PurgeChatImagesStartupService.IsAlreadyActive"/>, the pure classification logic
    /// extracted out of <see cref="PurgeChatImagesStartupService.ExecuteAsync"/> so it's testable without a
    /// real <see cref="DaprWorkflowClient"/>/Dapr sidecar — mirroring
    /// <c>AgentActorGatekeeper.IsWorkflowFinished</c>'s established pattern for the same kind of decision
    /// elsewhere in this codebase. There is no existing test pattern in this repo for
    /// <c>BackgroundService</c>/<c>DaprWorkflowClient</c> itself (nothing mocks/wraps
    /// <see cref="DaprWorkflowClient"/> directly — its <see cref="WorkflowState"/> return type has an
    /// internal constructor and can't be instantiated from here), so this test is deliberately scoped to
    /// just the extracted decision logic rather than the whole hosted service.
    /// </summary>
    public class PurgeChatImagesStartupServiceTests
    {
        [Theory]
        [InlineData(WorkflowRuntimeStatus.Running, true)]
        [InlineData(WorkflowRuntimeStatus.Suspended, true)]
        [InlineData(WorkflowRuntimeStatus.Pending, false)]
        [InlineData(WorkflowRuntimeStatus.Completed, false)]
        [InlineData(WorkflowRuntimeStatus.Failed, false)]
        [InlineData(WorkflowRuntimeStatus.Terminated, false)]
        public void IsAlreadyActive_RunningOrSuspended_ReturnsTrue_OthersReturnFalse(WorkflowRuntimeStatus status, bool expectedActive)
        {
            Assert.Equal(expectedActive, PurgeChatImagesStartupService.IsAlreadyActive(status));
        }

        [Fact]
        public void IsAlreadyActive_NullStatus_ReturnsFalse()
        {
            // A null status means the instance has never been created (the expected, common case on a
            // fresh environment/deployment — see the class remarks on GetWorkflowStateAsync's actual,
            // decompiled-and-verified behavior) — it must fall through to scheduling, not be treated as
            // "already active".
            Assert.False(PurgeChatImagesStartupService.IsAlreadyActive(null));
        }

        [Theory]
        [InlineData(WorkflowRuntimeStatus.Pending, true)]
        [InlineData(WorkflowRuntimeStatus.Running, true)]
        [InlineData(WorkflowRuntimeStatus.Suspended, true)]
        [InlineData(WorkflowRuntimeStatus.Completed, false)]
        [InlineData(WorkflowRuntimeStatus.Failed, false)]
        [InlineData(WorkflowRuntimeStatus.Terminated, false)]
        public void IsScheduledElsewhere_AfterFailedSchedule_NonTerminalStatusMeansAnotherReplicaWon(WorkflowRuntimeStatus status, bool expected)
        {
            // Two replicas starting together can both try to schedule the singleton instance; the loser's schedule
            // call fails, and a re-query that finds the instance Pending/Running/Suspended means it is not an error.
            Assert.Equal(expected, PurgeChatImagesStartupService.IsScheduledElsewhere(status));
        }

        [Fact]
        public void IsScheduledElsewhere_NoInstanceAfterFailedSchedule_IsAGenuineFailure()
        {
            Assert.False(PurgeChatImagesStartupService.IsScheduledElsewhere(null));
        }
    }
}
