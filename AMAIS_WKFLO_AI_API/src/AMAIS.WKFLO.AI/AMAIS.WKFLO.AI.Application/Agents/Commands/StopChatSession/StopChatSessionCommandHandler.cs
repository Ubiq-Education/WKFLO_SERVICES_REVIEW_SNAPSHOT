using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession
{
    /// <summary>
    /// US 7501: Stop is a hard cancel of every agent of the session. The turn usually runs on another replica, so the
    /// Stop is recorded (<c>chat-stop-{tenant}:{sessionId}</c>) and broadcast; each replica cancels its own turns of that
    /// (tenant, session) that started before <see cref="StopChatSessionResult.StoppedAt"/>. The chat is told straight
    /// away (typing off, idle, <c>ConversationStopped</c>) so the UI is back to idle without waiting for the turn to
    /// unwind. Everything is scoped by the caller tenant: a Stop never reaches another tenant's turns or approvals.
    /// Runs to the end once validation passed (no request token), so a client abort cannot leave it half-applied.
    /// </summary>
    public class StopChatSessionProcessor
    {
        private readonly IChatStopStateStore _stopStateStore;
        private readonly IChatStopBroadcaster _broadcaster;
        private readonly IChatTurnRegistry _turnRegistry;
        private readonly ToolApprovalSessionGate _toolApprovalSessionGate;
        private readonly IContactFormReplayStateService _contactFormReplayStateService;
        private readonly IChatNotificationService _notificationService;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<StopChatSessionProcessor> _logger;
        private readonly TenantContext _tenantContext;
        private readonly IHandOffQueueClearScheduler _handOffQueueClearScheduler;

        public StopChatSessionProcessor(IChatStopStateStore stopStateStore, IChatStopBroadcaster broadcaster, IChatTurnRegistry turnRegistry,
            ToolApprovalSessionGate toolApprovalSessionGate, IContactFormReplayStateService contactFormReplayStateService,
            IChatNotificationService notificationService, IDateTimeProvider dateTimeProvider, ILogger<StopChatSessionProcessor> logger,
            TenantContext tenantContext, IHandOffQueueClearScheduler handOffQueueClearScheduler)
        {
            _tenantContext = tenantContext;
            _handOffQueueClearScheduler = handOffQueueClearScheduler;
            _stopStateStore = stopStateStore;
            _broadcaster = broadcaster;
            _turnRegistry = turnRegistry;
            _toolApprovalSessionGate = toolApprovalSessionGate;
            _contactFormReplayStateService = contactFormReplayStateService;
            _notificationService = notificationService;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <param name="hostAgentId">The chat's own agent (tenant chat), when known.</param>
        /// <param name="hostShareId">The chat's share id (public chat), used to find its agent.</param>
        public async Task<Result> StopAsync(string sessionId, int? hostAgentId, string hostShareId)
        {
            // Not tied to the request: once validated, the Stop always runs to the end.
            var cancellationToken = CancellationToken.None;
            var tenantName = _tenantContext.TenantName;
            var stoppedAt = _dateTimeProvider.DateTimeOffsetUtcNow();
            _logger.LogInformation("Stop requested for session {SessionId} (tenant {TenantName}), stopped at {StoppedAt}", sessionId, tenantName, stoppedAt);

            // Durable first: it is what the checkpoints of every turn read if the broadcast is missed. Best effort: the
            // broadcast and the local cancel below still stop the running turns without it.
            var stateRecorded = false;
            try
            {
                await _stopStateStore.SetStoppedAsync(tenantName, sessionId, stoppedAt, cancellationToken);
                stateRecorded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Recording the stop of session {SessionId} failed", sessionId);
            }

            var signal = new ChatStopSignal(tenantName, sessionId, stoppedAt);
            var broadcastPublished = false;
            try
            {
                broadcastPublished = await _broadcaster.PublishAsync(signal, cancellationToken);
            }
            catch (Exception ex)
            {
                // The stored stop state still stops the turns at their next checkpoint.
                _logger.LogError(ex, "Broadcasting the stop of session {SessionId} failed", sessionId);
            }

            // This replica's own turns do not depend on the broadcast.
            var localTurnsCancelled = _turnRegistry.CancelTurnsStartedBefore(tenantName, sessionId, stoppedAt);

            await _notificationService.SendTypingIndicator(sessionId, false);
            await _notificationService.SendConversationStateUpdate(sessionId, "idle");
            await _notificationService.SendConversationStoppedAsync(sessionId, stoppedAt);

            // Approved change 5: a request still waiting for approval would otherwise resume the stopped run.
            var approvalsExpired = await _toolApprovalSessionGate.ExpireOpenApprovalsAsync(tenantName, sessionId, cancellationToken);

            // Approved change 6: a contact form submitted after the Stop must not replay the stopped question.
            var replayCleared = false;
            try
            {
                await _contactFormReplayStateService.ConsumePendingReplayAsync(sessionId, cancellationToken);
                replayCleared = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Clearing the deferred contact form replay of session {SessionId} failed", sessionId);
            }

            // Approved change 3: drop the hand-offs still queued for the session, in the background (not awaited, not
            // tied to the request's token): an actor busy waiting for a running hand-off only takes the clear call once
            // that (cancelled) run has ended, which must not hold the Stop response.
            var handOffClearScheduled = false;
            try
            {
                _handOffQueueClearScheduler.Schedule(sessionId, hostAgentId, hostShareId, stoppedAt);
                handOffClearScheduled = true;
            }
            catch (Exception ex)
            {
                // Each queued hand-off still checks the stop state before it starts.
                _logger.LogWarning(ex, "Starting to clear the queued hand-offs of session {SessionId} failed", sessionId);
            }

            _logger.LogInformation("Stop of session {SessionId} (tenant {TenantName}) at {StoppedAt} applied: {LocalTurnsCancelled} local turn(s) cancelled, " +
                "stop state recorded: {StopStateRecorded}, broadcast published: {BroadcastPublished}, {ApprovalsExpired} approval(s) expired, " +
                "contact form replay cleared: {ReplayCleared}, queued hand-off clear started: {HandOffClearScheduled}",
                sessionId, tenantName, stoppedAt, localTurnsCancelled, stateRecorded, broadcastPublished, approvalsExpired, replayCleared, handOffClearScheduled);

            return new SuccessResult<StopChatSessionResult>(new StopChatSessionResult { SessionId = sessionId, StoppedAt = stoppedAt });
        }
    }

    public class StopTenantChatSessionCommandHandler : IRequestHandler<StopTenantChatSessionCommand, Result>
    {
        private readonly StopChatSessionProcessor _processor;

        public StopTenantChatSessionCommandHandler(StopChatSessionProcessor processor)
        {
            _processor = processor;
        }

        public async ValueTask<Result> Handle(StopTenantChatSessionCommand command, CancellationToken cancellationToken) =>
            await _processor.StopAsync(command.SessionId, command.Id, hostShareId: null);
    }

    public class StopPublicChatSessionCommandHandler : IRequestHandler<StopPublicChatSessionCommand, Result>
    {
        private readonly StopChatSessionProcessor _processor;

        public StopPublicChatSessionCommandHandler(StopChatSessionProcessor processor)
        {
            _processor = processor;
        }

        public async ValueTask<Result> Handle(StopPublicChatSessionCommand command, CancellationToken cancellationToken) =>
            await _processor.StopAsync(command.SessionId, hostAgentId: null, command.ShareId);
    }
}
