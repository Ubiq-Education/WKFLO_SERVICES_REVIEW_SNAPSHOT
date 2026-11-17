using AMAIS.WKFLO.AI.Application.Agents.Workflows;
using Dapr.Workflow;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.HostedServices
{
    /// <summary>
    /// Schedules the singleton <see cref="PurgeChatImagesWorkflow"/> instance on startup, unless one is
    /// already Running/Suspended.
    /// </summary>
    /// <remarks>
    /// Investigated whether <see cref="DaprWorkflowClient.GetWorkflowStateAsync(string, bool, System.Threading.CancellationToken)"/>
    /// throws for an instance that has never been created (e.g. a fresh environment/deployment) by
    /// decompiling the pinned Dapr.Workflow 1.17.9 package: it does not throw for that case — it returns
    /// <c>null</c>. Internally, <c>WorkflowGrpcClient.GetWorkflowMetadataAsync</c> returns <c>null</c> both
    /// when the sidecar reports the instance doesn't exist and when the gRPC call itself throws an
    /// <c>RpcException</c> with <c>StatusCode.NotFound</c> (it logs that case, at <c>LogLevel.Error</c>,
    /// under category <c>Dapr.Workflow.Client.WorkflowGrpcClient</c>, message "Workflow instance
    /// '{InstanceId}' not found" — an SDK design choice, not a sign anything is actually wrong); and
    /// <see cref="DaprWorkflowClient"/>'s own <c>GetWorkflowStateAsync</c> additionally catches any other
    /// <c>RpcException</c> and also returns <c>null</c>. So a scary-looking Error-level log line with that
    /// exact text on every first-ever run is expected, harmless SDK-internal chatter — not an unhandled
    /// exception reaching this class — and does not by itself prevent <c>ScheduleNewWorkflowAsync</c> below
    /// from running. This matches the same understanding <see cref="AMAIS.WKFLO.AI.Application.Agents.Actors.AgentActor"/>
    /// and <see cref="AMAIS.WKFLO.AI.Application.Agents.Actors.AgentActorGatekeeper"/> already rely on
    /// elsewhere in this codebase ("a null query result legitimately means 'not persisted yet'").
    ///
    /// The try/catch around <c>GetWorkflowStateAsync</c> below is still kept, narrowed to just that call, as
    /// defense-in-depth: it protects against a future SDK version changing this "never throws" contract, or
    /// this class later being changed to depend on <c>IDaprWorkflowClient</c> instead of the concrete
    /// <see cref="DaprWorkflowClient"/> (the interface's own XML docs do not make the same "never throws"
    /// guarantee). A genuine failure there is logged distinctly from a genuine failure scheduling the
    /// workflow, rather than one catch-all swallowing both.
    /// </remarks>
    public class PurgeChatImagesStartupService : BackgroundService
    {
        private const string InstanceId = "purge-chat-images-singleton";

        private readonly DaprWorkflowClient _workflowClient;
        private readonly ILogger<PurgeChatImagesStartupService> _logger;

        public PurgeChatImagesStartupService(DaprWorkflowClient workflowClient, ILogger<PurgeChatImagesStartupService> logger)
        {
            _workflowClient = workflowClient;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            WorkflowState state;

            try
            {
                state = await _workflowClient.GetWorkflowStateAsync(InstanceId);
            }
            catch (Exception ex)
            {
                // Per the class remarks, GetWorkflowStateAsync itself is not expected to throw for a
                // not-yet-existing instance (that case returns null instead) — reaching here means something
                // genuinely unexpected happened (e.g. the Dapr sidecar is unreachable at a level the SDK's
                // own RpcException handling doesn't cover). Don't proceed to schedule against a client that
                // may not be usable; log and bail for this startup pass.
                _logger.LogError(ex, "Failed to query PurgeChatImagesWorkflow state for {InstanceId}.", InstanceId);
                return;
            }

            if (IsAlreadyActive(state?.RuntimeStatus))
            {
                _logger.LogInformation("PurgeChatImagesWorkflow already running as {InstanceId}, skipping start.", InstanceId);
                return;
            }

            try
            {
                await _workflowClient.ScheduleNewWorkflowAsync(
                    nameof(PurgeChatImagesWorkflow),
                    instanceId: InstanceId,
                    input: string.Empty);

                _logger.LogInformation("PurgeChatImagesWorkflow scheduled as {InstanceId}.", InstanceId);
            }
            catch (Exception ex)
            {
                await HandleScheduleFailureAsync(ex);
            }
        }

        /// <summary>
        /// Every replica runs this service on startup, so two replicas can both see "not active" and both try to
        /// schedule the same <see cref="InstanceId"/>; the loser's schedule call fails. Re-query the state: if the
        /// instance now exists and is active, another replica won the race and there is nothing to do; otherwise the
        /// failure is genuine and is logged as an error.
        /// </summary>
        private async Task HandleScheduleFailureAsync(Exception scheduleException)
        {
            WorkflowRuntimeStatus? status = null;
            try
            {
                status = (await _workflowClient.GetWorkflowStateAsync(InstanceId))?.RuntimeStatus;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-query PurgeChatImagesWorkflow state for {InstanceId} after a failed schedule.", InstanceId);
            }

            if (IsScheduledElsewhere(status))
            {
                _logger.LogInformation("PurgeChatImagesWorkflow {InstanceId} was scheduled by another replica ({Status}), skipping start.", InstanceId, status);
                return;
            }

            _logger.LogError(scheduleException, "Failed to schedule PurgeChatImagesWorkflow.");
        }

        /// <summary>
        /// After a failed schedule call: whether the instance exists in a non-terminal status (Pending, Running or
        /// Suspended), i.e. the failure was the duplicate-schedule race with another replica rather than a real error.
        /// </summary>
        public static bool IsScheduledElsewhere(WorkflowRuntimeStatus? status) =>
            status is WorkflowRuntimeStatus.Pending or WorkflowRuntimeStatus.Running or WorkflowRuntimeStatus.Suspended;

        /// <summary>
        /// Whether a queried workflow status counts as "already active" and should therefore NOT be
        /// (re)scheduled. A <c>null</c> status (the instance was never created, or the query call failed
        /// unexpectedly, is not applicable here since that path already returns above) falls through to
        /// scheduling, as do terminal statuses (Completed/Failed/Terminated) and Pending — only
        /// Running/Suspended block it. Extracted as a pure, static method — operating on the plain nullable
        /// enum rather than the SDK's <see cref="WorkflowState"/> (whose constructor is internal to the SDK
        /// and so cannot be instantiated from a test in this repo) — so this decision is directly
        /// unit-testable without a real <see cref="DaprWorkflowClient"/>/Dapr sidecar. Mirrors
        /// <see cref="AMAIS.WKFLO.AI.Application.Agents.Actors.AgentActorGatekeeper.IsWorkflowFinished"/>'s
        /// established pattern for the same kind of decision elsewhere in this codebase.
        /// </summary>
        public static bool IsAlreadyActive(WorkflowRuntimeStatus? status) =>
            status is WorkflowRuntimeStatus.Running or WorkflowRuntimeStatus.Suspended;
    }
}
