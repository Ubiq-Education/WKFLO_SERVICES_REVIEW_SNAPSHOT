using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop
{
    /// <summary>
    /// US 7501: safety net for a missed Stop broadcast. Reads <c>chat-stop-{tenant}:{sessionId}</c> ONCE, at a point the turn
    /// already passes (turn start, before each tool call). It is a single read, never a loop or a timer.
    /// </summary>
    public interface IChatStopCheckpoint
    {
        /// <summary>
        /// When the session was stopped after <paramref name="turnToken"/>'s turn started, cancels that turn (through
        /// <see cref="IChatTurnRegistry"/>, so it is reported as a Stop) and throws <see cref="OperationCanceledException"/>.
        /// </summary>
        /// <param name="checkpoint">Where the turn reads the stop state (one of <see cref="ChatStopCheckpoints"/>), for the logs.</param>
        Task ThrowIfStoppedAsync(string tenantName, string sessionId, string checkpoint, CancellationToken turnToken);
    }

    /// <summary>The names of the points where a turn reads the stop state, as logged when one of them stops a turn.</summary>
    public static class ChatStopCheckpoints
    {
        public const string TurnStart = "turn start";
        public const string ToolCall = "tool call";
        public const string HandOffPublish = "hand-off publish";
        public const string HandOffStart = "hand-off start";
        public const string AgentInvocationStart = "agent invocation start";
    }

    public class ChatStopCheckpoint : IChatStopCheckpoint
    {
        private readonly IChatStopStateStore _stateStore;
        private readonly IChatTurnRegistry _turnRegistry;
        private readonly ILogger<ChatStopCheckpoint> _logger;

        public ChatStopCheckpoint(IChatStopStateStore stateStore, IChatTurnRegistry turnRegistry, ILogger<ChatStopCheckpoint> logger)
        {
            _stateStore = stateStore;
            _turnRegistry = turnRegistry;
            _logger = logger;
        }

        public async Task ThrowIfStoppedAsync(string tenantName, string sessionId, string checkpoint, CancellationToken turnToken)
        {
            turnToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            DateTimeOffset? stoppedAt;
            try
            {
                stoppedAt = await _stateStore.GetStoppedAtAsync(tenantName, sessionId, turnToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only a safety net: the broadcast is the primary path, so a failed read never fails the turn.
                _logger.LogWarning(ex, "Reading the stop state of session {SessionId} failed", sessionId);
                return;
            }

            if (stoppedAt is { } value)
            {
                // Logged only when the read actually stops something: the broadcast missed this replica.
                var cancelled = _turnRegistry.CancelTurnsStartedBefore(tenantName, sessionId, value);
                if (cancelled > 0)
                    _logger.LogInformation("Stop checkpoint {Checkpoint} cancelled {CancelledTurns} turn(s) of session {SessionId} (tenant {TenantName}), stopped at {StoppedAt}",
                        checkpoint, cancelled, sessionId, tenantName, value);
            }

            turnToken.ThrowIfCancellationRequested();
        }
    }
}
