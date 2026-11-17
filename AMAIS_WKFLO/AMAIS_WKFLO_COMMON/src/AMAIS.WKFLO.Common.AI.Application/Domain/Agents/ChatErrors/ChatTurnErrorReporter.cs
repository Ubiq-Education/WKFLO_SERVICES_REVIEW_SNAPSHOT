using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors
{
    /// <summary>Task 7961: the chat turn running in this scope, as the turn's choke point registered it.</summary>
    public sealed record ChatTurnErrorTurn
    {
        public string TenantName { get; init; }

        public string SessionId { get; init; }

        /// <summary>The turn's id (the workflow instance id): the UI shows one message per turn.</summary>
        public string TurnId { get; init; }

        public DateTimeOffset? TurnStartedAt { get; init; }

        /// <summary>Reads the turn's own Stop flag at the time of the report.</summary>
        public Func<bool> IsStopRequested { get; init; }
    }

    /// <summary>
    /// Task 7961 (US 7959): reports tool problems of the running turn to the chat as they happen. They are non-terminal
    /// warnings (the turn goes on without that tool), sent once per tool per turn. Scoped: the turn's choke point
    /// (<c>BaseWorkflowActivity</c>) registers the turn with <see cref="BeginTurn"/>; outside a registered turn a warning is
    /// only recorded in <see cref="IChatTurnFailureState"/> and nothing is sent.
    /// </summary>
    public interface IChatTurnErrorReporter
    {
        /// <summary>The registered turn, or null.</summary>
        ChatTurnErrorTurn Turn { get; }

        /// <summary>The agent answering in this scope, when known (set by the agent run).</summary>
        ChatTurnAgent CurrentAgent { get; }

        void BeginTurn(ChatTurnErrorTurn turn);

        void SetCurrentAgent(ChatTurnAgent agent);

        /// <summary>
        /// Sends <paramref name="code"/> (a warning code, see <see cref="ChatErrorCodes.IsWarning"/>) for the tool
        /// <paramref name="toolKey"/>, at most once per (agent, code, tool) in this turn. The failing agent defaults to
        /// <see cref="CurrentAgent"/>. Returns true when the chat was notified. Never throws.
        /// </summary>
        Task<bool> ReportWarningAsync(string code, string toolKey, int? agentId = null, string agentName = null);

        /// <summary>
        /// PR 16989 review: true the first time the tool <paramref name="toolKey"/> fails in the registered turn, so its
        /// "failed" tool update is sent once per tool per turn. Always true outside a registered turn (nothing to scope it to).
        /// </summary>
        bool TryClaimToolFailure(string toolKey);
    }

    public sealed class ChatTurnErrorReporter : IChatTurnErrorReporter
    {
        private readonly IChatTurnErrorNotifier _notifier;
        private readonly IChatTurnFailureState _failureState;
        private readonly ILogger<ChatTurnErrorReporter> _logger;
        private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _failedTools = new(StringComparer.Ordinal);
        private ChatTurnErrorTurn _turn;
        private ChatTurnAgent _currentAgent;

        public ChatTurnErrorReporter(IChatTurnErrorNotifier notifier, IChatTurnFailureState failureState, ILogger<ChatTurnErrorReporter> logger)
        {
            _notifier = notifier;
            _failureState = failureState;
            _logger = logger;
        }

        public ChatTurnErrorTurn Turn => Volatile.Read(ref _turn);

        public ChatTurnAgent CurrentAgent => Volatile.Read(ref _currentAgent);

        public void BeginTurn(ChatTurnErrorTurn turn) => Volatile.Write(ref _turn, turn);

        public void SetCurrentAgent(ChatTurnAgent agent) => Volatile.Write(ref _currentAgent, agent);

        public bool TryClaimToolFailure(string toolKey)
        {
            var turn = Turn;
            if (turn == null || string.IsNullOrWhiteSpace(turn.SessionId))
                return true;

            return _failedTools.TryAdd(toolKey ?? string.Empty, 0);
        }

        public async Task<bool> ReportWarningAsync(string code, string toolKey, int? agentId = null, string agentName = null)
        {
            try
            {
                var agent = CurrentAgent;
                var failure = agentId.HasValue && agentId != agent?.AgentId
                    ? ChatTurnFailure.Create(code, agentId, agentName)
                    : ChatTurnFailure.Create(code, agent?.AgentId ?? agentId, agent?.AgentName ?? agentName, agent?.ChatbotIconUrl);

                // Only tool problems are reported mid-turn; anything else belongs to the turn's choke point.
                if (failure.Severity != ChatErrorSeverity.Warning)
                {
                    _failureState?.Record(failure);
                    return false;
                }

                var turn = Turn;
                if (turn == null || string.IsNullOrWhiteSpace(turn.SessionId))
                {
                    // No chat to tell (e.g. a run outside a registered turn): keep it for whoever owns this scope.
                    _failureState?.Record(failure);
                    return false;
                }

                var key = $"{failure.AgentId}:{failure.Code}:{toolKey}";
                if (!_reported.TryAdd(key, 0))
                    return false;

                return await _notifier.NotifyAsync(new ChatTurnErrorNotification
                {
                    TenantName = turn.TenantName,
                    SessionId = turn.SessionId,
                    TurnId = turn.TurnId,
                    TurnStartedAt = turn.TurnStartedAt,
                    StopRequested = turn.IsStopRequested?.Invoke() == true,
                    Failure = failure,
                    DedupeKey = $"{turn.TurnId}:{key}"
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reporting tool warning {ErrorCode} for tool {ToolKey} failed", code, toolKey);
                return false;
            }
        }
    }
}
