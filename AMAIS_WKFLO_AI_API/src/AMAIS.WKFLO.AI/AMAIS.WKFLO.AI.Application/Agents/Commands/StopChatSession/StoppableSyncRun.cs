using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession
{
    /// <summary>
    /// US 7501: makes a synchronous run (the Run endpoints) stoppable like a queued turn: it is registered as a chat turn
    /// of its (tenant, session), reads the stop state once at its start, and runs with the turn's Stop token. The HTTP
    /// request's token is never used, so a client disconnect still lets the run finish and save.
    /// </summary>
    public class StoppableSyncRun
    {
        private readonly IChatTurnRegistry _turnRegistry;
        private readonly IChatStopCheckpoint _stopCheckpoint;
        private readonly IChatNotificationService _notificationService;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly IChatSessionOwnerStore _sessionOwnerStore;
        private readonly ILogger<StoppableSyncRun> _logger;

        public StoppableSyncRun(IChatTurnRegistry turnRegistry, IChatStopCheckpoint stopCheckpoint, IChatNotificationService notificationService,
            IDateTimeProvider dateTimeProvider, TenantContext tenantContext, ChatContext chatContext, UserContext userContext,
            IChatSessionOwnerStore sessionOwnerStore, ILogger<StoppableSyncRun> logger)
        {
            _chatContext = chatContext;
            _userContext = userContext;
            _sessionOwnerStore = sessionOwnerStore;
            _turnRegistry = turnRegistry;
            _stopCheckpoint = stopCheckpoint;
            _notificationService = notificationService;
            _dateTimeProvider = dateTimeProvider;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        /// <summary>
        /// Runs <paramref name="run"/> as a turn of <paramref name="sessionId"/>. Returns <c>Stopped = true</c> (and no
        /// result) when a Stop cancelled it, either at its start or while it ran. Without a session it is not stoppable
        /// and runs as before. <paramref name="owner"/> (the chat; the user is added here) is recorded as the session's
        /// owner when it has none yet, so only this chat and user can stop its first turn.
        /// </summary>
        public async Task<(T Result, bool Stopped)> RunAsync<T>(string sessionId, ChatSessionOwner owner, Func<CancellationToken, Task<T>> run)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return (await run(CancellationToken.None), false);

            var tenantName = _tenantContext.TenantName;
            if (owner != null)
            {
                owner.UserId = _userContext?.Guid;
                await _sessionOwnerStore.RecordAsync(tenantName, sessionId, owner);
            }

            using var turn = _turnRegistry.Register(tenantName, sessionId, _dateTimeProvider.DateTimeOffsetUtcNow());
            if (_chatContext != null)
                _chatContext.TurnStartedAt = turn.StartedAt;
            T result = default;
            var cutShort = false;
            try
            {
                await _stopCheckpoint.ThrowIfStoppedAsync(tenantName, sessionId, ChatStopCheckpoints.TurnStart, turn.Token);
                result = await run(turn.Token);
            }
            catch (OperationCanceledException) when (turn.StopRequested)
            {
                cutShort = true;
            }

            // Stopped only when the run was cut short: thrown out by the Stop, or it swallowed the cancellation and has no
            // answer. An answer the run finished (and saved) before the Stop was seen is returned as usual.
            if (!turn.StopRequested || (!cutShort && result is not null))
                return (result, false);

            _logger.LogInformation("Synchronous run of session {SessionId} (tenant {TenantName}) stopped by the user", sessionId, tenantName);

            // Same as a stopped queued turn: the Stop endpoint already sent these, sent again in case the run's own
            // events arrived after them - unless a newer turn of the session already runs.
            if (!_turnRegistry.HasRunningTurnStartedAfter(tenantName, sessionId, turn.StartedAt))
            {
                // Best effort, as in the Stop command: the Stop already told the chat, so a SignalR failure here must not
                // turn the stopped response into an error.
                try
                {
                    await _notificationService.SendTypingIndicator(sessionId, false);
                    await _notificationService.SendConversationStateUpdate(sessionId, "idle");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telling the chat that the stopped run of session {SessionId} is idle failed", sessionId);
                }
            }

            return (default, true);
        }
    }
}
