using AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent;
using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using Dapr.Actors.Runtime;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Actors
{
    /// <summary>
    /// Per-session/agent gatekeeper (story #6480 / task #6589) that runs <see cref="AgentEventWorkflow"/>
    /// instances one at a time for a given actor ID. The only persisted state is the queue of pending
    /// message keys — there is deliberately no separate "is a workflow running" flag (see
    /// <see cref="AgentActorGatekeeper"/>'s remarks for why an earlier revision's boolean-state design
    /// caused a real race). Instead, <see cref="ReceiveReminderAsync"/> blocks its own turn on
    /// <see cref="DaprWorkflowClient.WaitForWorkflowCompletionAsync"/> for the head-of-queue item before
    /// moving on — Dapr's turn-based per-actor-ID concurrency (only one call executes at a time for this
    /// actor ID) is what makes "only one workflow at a time" true, not a flag we'd have to keep in sync.
    ///
    /// <see cref="EnqueueEventAsync"/> only queues the key and (re-)arms the reminder — it never
    /// schedules a workflow itself. Before scheduling the head-of-queue item, <see cref="ReceiveReminderAsync"/>
    /// checks <see cref="DaprWorkflowClient.GetWorkflowStateAsync"/> first: if the instance already
    /// exists (e.g. this actor restarted mid-wait and the queue still has the same head item), it skips
    /// straight to waiting instead of re-scheduling — turning that recovery case into a normal path
    /// instead of a duplicate-schedule error. The reminder disarms itself once the queue drains, and is
    /// kept as a ~5s periodic dead-man's-switch fallback in case an immediate re-arm is ever missed.
    ///
    /// US 7501: a Stop drops the session's pending hand-offs (<see cref="ClearQueueAsync"/>), and the head item is
    /// checked once against the session's stop state before it is scheduled, so a hand-off queued just before the
    /// Stop does not start.
    /// </summary>
    public class AgentActor : Actor, IAgentActor, IRemindable
    {
        private const string ReminderName = "check";
        private const string QueueStateName = "queue";
        private static readonly TimeSpan ReminderPeriod = TimeSpan.FromSeconds(5);

        // Bounds how long a single reminder tick will wait for one AgentEventWorkflow to finish. This
        // wraps one LLM agent invocation, not an eternal workflow, so a few minutes is generous. If it's
        // ever exceeded, we give up on tracking that item (log + move on) rather than block this actor's
        // turn indefinitely — the underlying Dapr workflow keeps running in the background either way,
        // this only affects how long the gatekeeper itself waits before considering the next queued item.
        private static readonly TimeSpan MaxWorkflowWait = TimeSpan.FromMinutes(5);

        private readonly DaprWorkflowClient _workflowClient;
        private readonly ILogger<AgentActor> _logger;
        private readonly IHandOffQueueStopGuard _stopGuard;
        private readonly IDroppedHandOffMarkerSaver _droppedHandOffMarkerSaver;

        public AgentActor(ActorHost host, DaprWorkflowClient workflowClient, ILogger<AgentActor> logger, IHandOffQueueStopGuard stopGuard,
            IDroppedHandOffMarkerSaver droppedHandOffMarkerSaver)
            : base(host)
        {
            _stopGuard = stopGuard;
            _droppedHandOffMarkerSaver = droppedHandOffMarkerSaver;
            _workflowClient = workflowClient;
            _logger = logger;
        }

        public async Task EnqueueEventAsync(string messageKey)
        {
            var queue = await GetQueueAsync();

            if (!AgentActorGatekeeper.TryEnqueue(queue, messageKey))
            {
                _logger.LogInformation("AgentActor {ActorId}: key {Key} already queued, ignoring duplicate", Id, messageKey);
                return;
            }

            await StateManager.SetStateAsync(QueueStateName, queue);

            // Fire (almost) immediately for a cold queue, then re-check every 5s as a fallback.
            await RegisterReminderAsync(ReminderName, null, TimeSpan.Zero, ReminderPeriod);
        }

        public async Task<int> ClearQueueAsync(DateTimeOffset stoppedAt)
        {
            var queue = await GetQueueAsync();
            if (queue.Count == 0)
                return 0;

            var queuedAtByKey = await _stopGuard.GetQueuedAtAsync(queue);
            var dropped = AgentActorGatekeeper.DropQueuedBeforeStop(queue, queuedAtByKey, stoppedAt);
            if (dropped.Count == 0)
                return 0;

            await StateManager.SetStateAsync(QueueStateName, queue);

            foreach (var key in dropped)
                await _stopGuard.DeleteQueuedMessageAsync(key);

            if (queue.Count == 0)
                await UnregisterReminderAsync(ReminderName);

            _logger.LogInformation("AgentActor {ActorId}: dropped {Count} queued hand-off(s) after a Stop", Id, dropped.Count);
            return dropped.Count;
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            if (reminderName != ReminderName) return;

            var queue = await GetQueueAsync();

            if (!AgentActorGatekeeper.TryDequeueNextCandidate(queue, out var key, out var instanceId))
            {
                // Empty, or only had malformed entries (now dropped) — persist the cleanup and stop.
                await StateManager.SetStateAsync(QueueStateName, queue);
                await UnregisterReminderAsync(ReminderName);
                return;
            }

            await ProcessCandidateAsync(key, instanceId);

            // Remove by value, not by index: RemoveAt(0) assumes the candidate found above is still at
            // the head when we get here, with nothing touching the queue across the ProcessCandidateAsync
            // await — an assumption that failed in practice (queue length was 0), crashing this turn with
            // ArgumentOutOfRangeException. Remove(key) is a no-op if it's already gone.
            queue.Remove(key);
            await StateManager.SetStateAsync(QueueStateName, queue);

            if (queue.Count > 0)
                await RegisterReminderAsync(ReminderName, null, TimeSpan.Zero, ReminderPeriod);
            else
                await UnregisterReminderAsync(ReminderName);
        }

        private async Task ProcessCandidateAsync(string key, string instanceId)
        {
            try
            {
                // The instance ID is derived from the queued key, and the workflow is only created below
                // (ScheduleNewWorkflowAsync), so on the normal path this lookup finds nothing. Dapr has no
                // separate "exists" call: on a miss the SDK logs "Workflow instance '{InstanceId}' not found"
                // at Error (LogGetWorkflowMetadataInstanceNotFound) and returns null — expected, not a failure.
                // That log is silenced in the AI API's Program.cs (Dapr.Workflow.Client.WorkflowGrpcClient filter).
                var wfState = await _workflowClient.GetWorkflowStateAsync(instanceId);

                // wfState == null means "never scheduled" in the normal case. Non-null-but-not-yet-terminal
                // means this actor previously scheduled it and is now recovering (e.g. after a restart) —
                // skip straight to waiting instead of scheduling a duplicate.
                if (wfState == null)
                {
                    // US 7501: one stop-state read before a hand-off starts, so one queued before a Stop never runs.
                    var droppingStop = await _stopGuard.GetDroppingStopAsync(Id.GetId(), key);
                    if (droppingStop != null)
                    {
                        _logger.LogInformation("AgentActor {ActorId}: session stopped, dropping queued hand-off {Key}", Id, key);
                        await _stopGuard.DeleteQueuedMessageAsync(key);

                        // US 7502: nothing ran for this hand-off, so the chat agent's history keeps the Stop's marker.
                        await _droppedHandOffMarkerSaver.SaveAsync(new DroppedHandOffStop(droppingStop.TenantName, droppingStop.SessionId, droppingStop.StoppedAt)
                        {
                            DroppedAgentId = AgentActorGatekeeper.TryGetAgentId(Id.GetId(), out var droppedAgentId) ? droppedAgentId : null
                        });
                        return;
                    }

                    await _workflowClient.ScheduleNewWorkflowAsync(nameof(AgentEventWorkflow), instanceId, key);
                }

                if (!AgentActorGatekeeper.IsWorkflowFinished(wfState?.RuntimeStatus))
                {
                    using var cts = new CancellationTokenSource(MaxWorkflowWait);
                    await _workflowClient.WaitForWorkflowCompletionAsync(instanceId, cancellation: cts.Token);
                }
            }
            catch (Exception ex)
            {
                // Best-effort, no retry — consistent with this pub/sub path's existing semantics
                // (AgentEventWorkflowActivity itself is one-shot with no retry either). A timeout here
                // just means we stop waiting; the real workflow, if still running, finishes on its own.
                _logger.LogWarning(ex, "AgentActor {ActorId}: failed to run/await workflow for instance {InstanceId}, giving up on this event", Id, instanceId);
            }
        }

        private async Task<List<string>> GetQueueAsync()
        {
            var existing = await StateManager.TryGetStateAsync<List<string>>(QueueStateName);
            return existing.HasValue ? existing.Value : [];
        }
    }
}
