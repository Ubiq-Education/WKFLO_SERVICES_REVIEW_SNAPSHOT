using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.AI.Application.Agents.Invocation;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.SignalR;
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent
{
    /// <summary>
    /// Loads the queued <see cref="AgentEventMessage"/> by state-store key, invokes
    /// <see cref="IAgentInvoker"/> directly, and deletes the state entry once read. Not based on
    /// <c>BaseWorkflowActivity</c>: that base's <c>BaseRunAsync&lt;V&gt;</c> requires the queued payload
    /// to itself be a MediatR command (<c>IWkfloQueueMessageCommand</c>) dispatched via
    /// <c>IWkfloMediator.Send</c> — <see cref="AgentEventMessage"/> is plain data consumed via
    /// <see cref="IAgentInvoker"/> instead, the same reason <c>InvokeAgentWorkflowActivity</c> (the
    /// multi-agent orchestrator's activity) doesn't use it either. One-shot, no idempotency tracking
    /// and no retry — same semantics <c>AgentEventSubscriber</c> had before this activity existed
    /// (failures are logged, not retried by Dapr).
    ///
    /// US 7501: the run is registered as a chat turn of its session, so a Stop (broadcast to every replica, or
    /// found by the checkpoint read at its start) cancels it. A stopped run reports <see cref="SubagentActivityStatus.Cancelled"/>.
    ///
    /// US 7959 / Task 7963: a failed hand-off tells the chat with codes only, never exception text: its "failed" card
    /// carries <see cref="SubagentActivityUpdate.ErrorCode"/>, its thinking bubble stops and a non-terminal marker is sent
    /// (the chat's own agent already answered); the chat then leaves "processing". Nothing for a Stop.
    /// </summary>
    public class AgentEventWorkflowActivity : WorkflowActivity<string, bool>
    {
        private readonly DaprClient _daprClient;
        private readonly IAgentInvoker _agentInvoker;
        private readonly IMasterDataDbContext _db;
        private readonly IChatNotificationService _notificationService;
        private readonly ILogger<AgentEventWorkflowActivity> _logger;
        private readonly IChatTurnRegistry _turnRegistry;
        private readonly IChatStopCheckpoint _stopCheckpoint;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatStopStateStore _stopStateStore;
        private readonly IDroppedHandOffMarkerSaver _droppedHandOffMarkerSaver;
        private readonly IChatAgentFailureReporter _failureReporter;
        private readonly IChatTurnErrorReporter _chatTurnErrorReporter;
        private readonly ISessionWorkingStateStore _workingState;

        /// <param name="failureReporter">US 7959 / Task 7963: tells the chat the hand-off failed. Null: only its failed card is sent.</param>
        /// <param name="chatTurnErrorReporter">US 7959 / Task 7963: lets the run report its tool warnings live. Null: they are only recorded.</param>
        /// <param name="workingState">US 7506: clears the hand-off's queued working-state entry when the run ends before its turn
        /// is registered (the turn clears it otherwise). Null: the entry is left to expire.</param>
        public AgentEventWorkflowActivity(DaprClient daprClient, IAgentInvoker agentInvoker, IMasterDataDbContext db,
            IChatNotificationService notificationService, ILogger<AgentEventWorkflowActivity> logger,
            IChatTurnRegistry turnRegistry, IChatStopCheckpoint stopCheckpoint, IDateTimeProvider dateTimeProvider,
            IChatStopStateStore stopStateStore, IDroppedHandOffMarkerSaver droppedHandOffMarkerSaver,
            IChatAgentFailureReporter failureReporter = null, IChatTurnErrorReporter chatTurnErrorReporter = null,
            ISessionWorkingStateStore workingState = null)
        {
            _workingState = workingState;
            _failureReporter = failureReporter;
            _chatTurnErrorReporter = chatTurnErrorReporter;
            _stopStateStore = stopStateStore;
            _droppedHandOffMarkerSaver = droppedHandOffMarkerSaver;
            _turnRegistry = turnRegistry;
            _stopCheckpoint = stopCheckpoint;
            _dateTimeProvider = dateTimeProvider;
            _daprClient = daprClient;
            _agentInvoker = agentInvoker;
            _db = db;
            _notificationService = notificationService;
            _logger = logger;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, string key)
        {
            WkfloQueueMessage<AgentEventMessage> queueMessage;
            try
            {
                queueMessage = await _daprClient.GetStateAsync<WkfloQueueMessage<AgentEventMessage>>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, key);
            }
            catch (Exception ex)
            {
                // US 7506: the hand-off's queued working-state entry cannot be cleared here: it lives under the session's
                // key, and the session is only known from the payload that failed to load. It expires on its own
                // (SessionWorkingStateConsts.HandOffTimeToLive).
                _logger.LogError(ex, "AgentEventWorkflowActivity: failed to load queued event for key {Key}", key);
                return false;
            }

            var eventMessage = queueMessage?.Command;

            if (eventMessage == null || eventMessage.AgentId <= 0)
            {
                _logger.LogWarning("AgentEventWorkflowActivity: no queued event found (or invalid AgentId) for key {Key}", key);

                // US 7506: no turn will run for this hand-off, so nothing else removes its queued entry.
                await ClearQueuedWorkAsync(queueMessage?.TenantContext?.TenantName, eventMessage?.SessionId, key);
                return true;
            }

            var success = false;
            var awaitingApproval = false;
            var cancelled = false;
            var invoked = false;
            string errorCode = null;
            IChatTurn turn = null;
            var turnId = context?.InstanceId ?? key;
            AgentInvocationRequest request = null;
            try
            {
                // Bug 7722: lets a tool approval requested in this run report its later states with the same action.
                if (queueMessage.ChatContext != null)
                    queueMessage.ChatContext.SubagentActivityAction = eventMessage.EventType;

                request = new AgentInvocationRequest
                {
                    AgentId = eventMessage.AgentId,
                    AgentRole = eventMessage.AgentRole,
                    Input = eventMessage.Input,
                    SessionId = eventMessage.SessionId,
                    ChainOfThought = false,
                    TenantContext = queueMessage.TenantContext,
                    ChatContext = queueMessage.ChatContext,
                    UserContext = queueMessage.UserContext,
                    AgentSharableContext = queueMessage.AgentSharableContext,
                    RootIsWidget = eventMessage.RootIsWidget,
                    ApprovalRootAgentId = eventMessage.ApprovalRootAgentId
                };

                // US 7501: a hand-off queued before the Stop counts as started before it, even when it only runs now.
                var cancellationToken = CancellationToken.None;
                if (!string.IsNullOrWhiteSpace(eventMessage.SessionId))
                {
                    // A Stop only reaches turns of its own tenant: the run is registered under the hand-off's tenant.
                    var tenantName = queueMessage.TenantContext?.TenantName;
                    // US 7506: the turn takes over the hand-off's "queued" working-state entry (added when it was published).
                    turn = _turnRegistry.Register(tenantName, eventMessage.SessionId, queueMessage.QueuedAt ?? _dateTimeProvider.DateTimeOffsetUtcNow(), queuedWorkId: key);
                    cancellationToken = turn.Token;

                    // Its own hand-offs inherit this start, which is the requesting turn's (the whole chain stops together).
                    if (queueMessage.ChatContext != null)
                        queueMessage.ChatContext.TurnStartedAt = turn.StartedAt;

                    // Single read of the stop state: covers a Stop this replica's subscription missed.
                    await _stopCheckpoint.ThrowIfStoppedAsync(tenantName, eventMessage.SessionId, ChatStopCheckpoints.HandOffStart, cancellationToken);

                    // US 7959 / Task 7963: the hand-off's tool warnings reach the chat as they happen.
                    var runTurn = turn;
                    _chatTurnErrorReporter?.BeginTurn(new ChatTurnErrorTurn
                    {
                        TenantName = tenantName,
                        SessionId = eventMessage.SessionId,
                        TurnId = turnId,
                        TurnStartedAt = runTurn.StartedAt,
                        IsStopRequested = () => runTurn.StopRequested
                    });
                }

                invoked = true;
                var result = await _agentInvoker.InvokeAsync(request, cancellationToken);

                success = result.Success;
                awaitingApproval = result.AwaitingApproval;

                if (!success)
                {
                    errorCode = result.ErrorCode ?? ChatErrorCodes.ChatTurnFailed;
                    _logger.LogWarning(
                        "AgentEventWorkflowActivity: agent {AgentId} (role {AgentRole}) failed to react to event {EventType} with {ErrorCode}: {ErrorMessage}",
                        eventMessage.AgentId, eventMessage.AgentRole, eventMessage.EventType, errorCode, result.ErrorMessage);
                }
            }
            catch (OperationCanceledException) when (turn?.StopRequested == true)
            {
                // US 7501: stopped by the person - neither a failure nor something to retry.
                _logger.LogInformation("AgentEventWorkflowActivity: hand-off to agent {AgentId} for event {EventType} in session {SessionId} (tenant {TenantName}) cancelled by the Stop, phase {StopPhase}",
                    eventMessage.AgentId, eventMessage.EventType, eventMessage.SessionId, queueMessage.TenantContext?.TenantName,
                    invoked ? "running" : "before run");

                cancelled = true;
                success = false;

                // US 7502: the chat agent's history keeps the Stop's marker (when it has a saved history), the same as for a
                // hand-off dropped from the queue; the dedupe on stoppedAt keeps one marker per Stop. A hand-off that never
                // ran saves nothing in its own history; one stopped while running saved its own pre-turn history plus the
                // marker (only when it already had one).
                await SaveMarkerForTheChatAsync(queueMessage.TenantContext?.TenantName, eventMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AgentEventWorkflowActivity: failed to invoke agent {AgentId} for event {EventType}",
                    eventMessage.AgentId, eventMessage.EventType);

                success = false;

                // US 7959 / Task 7963: a code only; the exception is in the log above.
                errorCode = ChatErrorClassifier.Classify(ex, stopRequested: false) ?? ChatErrorCodes.ChatTurnFailed;
            }
            finally
            {
                turn?.Dispose();

                // This is the eventual completion of the "started" update AgentEventPublisherPlugin sent
                // when this hand-off was first published — the only place that update's outcome (success
                // vs. failure) is actually known, since the invocation itself is fire-and-forget.
                try
                {
                    var finalStatus = FinalStatus(success, awaitingApproval, cancelled);
                    if (finalStatus == SubagentActivityStatus.Failed)
                    {
                        await ReportFailureAsync(queueMessage, eventMessage, turn, turnId, errorCode, invoked);
                    }
                    else if (finalStatus != null)
                    {
                        var chatbotIconUrl = await AgentChatIconHelper.GetChatbotIconUrlAsync(_db, eventMessage.AgentId, eventMessage.RootIsWidget);
                        await _notificationService.SendSubagentActivityUpdate(
                            eventMessage.SessionId, eventMessage.AgentId, eventMessage.AgentRole, finalStatus,
                            chatbotIconUrl, action: eventMessage.EventType);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AgentEventWorkflowActivity: failed to send subagent activity update for key {Key}", key);
                }

                try
                {
                    await _daprClient.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AgentEventWorkflowActivity: failed to delete queued event state for key {Key}", key);
                }
            }

            // A stopped run is finished, not failed.
            return success || cancelled;
        }

        // Without the session the entry cannot be located; it then expires on its own. Best effort (the store never throws).
        private Task ClearQueuedWorkAsync(string tenantName, string sessionId, string key) =>
            _workingState == null || string.IsNullOrWhiteSpace(sessionId)
                ? Task.CompletedTask
                : _workingState.ClearWorkingAsync(tenantName, sessionId, [key]);

        /// <summary>
        /// US 7959 / Task 7963: the hand-off failed. The chat is told once (typing-off for the agent, a non-terminal marker,
        /// back to "idle" when this run had set it to "processing"), then the agent's card turns "failed" with the code.
        /// Nothing when the user stopped the session meanwhile.
        /// </summary>
        private async Task ReportFailureAsync(WkfloQueueMessage<AgentEventMessage> queueMessage, AgentEventMessage eventMessage, IChatTurn turn,
            string turnId, string errorCode, bool invoked)
        {
            var code = ChatAgentFailure.ToAgentErrorCode(errorCode);
            var chatbotIconUrl = await AgentChatIconHelper.GetChatbotIconUrlAsync(_db, eventMessage.AgentId, eventMessage.RootIsWidget);

            var notified = _failureReporter == null || await _failureReporter.ReportAsync(new ChatAgentFailure
            {
                TenantName = queueMessage.TenantContext?.TenantName,
                SessionId = eventMessage.SessionId,
                TurnId = turnId,
                TurnStartedAt = turn?.StartedAt ?? queueMessage.QueuedAt,
                StopRequested = turn?.StopRequested == true,
                ErrorCode = code,
                AgentId = eventMessage.AgentId,
                AgentName = eventMessage.AgentRole,
                ChatbotIconUrl = chatbotIconUrl,
                IsTerminal = false,
                EnsureIdle = invoked
            });

            if (notified)
            {
                await _notificationService.SendSubagentActivityFailedUpdate(
                    eventMessage.SessionId, eventMessage.AgentId, eventMessage.AgentRole, code, chatbotIconUrl, action: eventMessage.EventType);
            }
        }

        private async Task SaveMarkerForTheChatAsync(string tenantName, AgentEventMessage eventMessage)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(eventMessage.SessionId))
                    return;

                // The Stop's own stoppedAt, so the marker dedupes with any other marker of the same Stop.
                var stoppedAt = await _stopStateStore.GetStoppedAtAsync(tenantName, eventMessage.SessionId, CancellationToken.None);
                if (stoppedAt is not { } value)
                    return;

                await _droppedHandOffMarkerSaver.SaveAsync(new DroppedHandOffStop(tenantName, eventMessage.SessionId, value)
                {
                    DroppedAgentId = eventMessage.AgentId
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AgentEventWorkflowActivity: saving the Stopped marker for the stopped hand-off of agent {AgentId} failed", eventMessage.AgentId);
            }
        }

        // Bug 7722: a run that stopped on a pending tool approval is not finished yet. Its "awaiting_approval" was
        // already sent when the approval was requested (before the card), so nothing is sent here; the resume after
        // the decision (or the expiry) sends the next status.
        // US 7501: a run stopped by the person reports "cancelled" instead of "completed"/"failed".
        internal static string FinalStatus(bool success, bool awaitingApproval, bool cancelled = false) =>
            cancelled ? SubagentActivityStatus.Cancelled
            : !success ? SubagentActivityStatus.Failed
            : awaitingApproval ? null
            : SubagentActivityStatus.Completed;
    }
}
