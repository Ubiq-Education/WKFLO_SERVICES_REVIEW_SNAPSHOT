using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.ChatErrors
{
    /// <summary>An agent of a multi-agent turn (serializable: travels as workflow activity input).</summary>
    public class ChatAgentRef
    {
        public int AgentId { get; set; }

        public string AgentName { get; set; }
    }

    /// <summary>
    /// US 7959 / Task 7963: the failure of one agent of a multi-agent run (orchestrator step, hand-off, tool approval
    /// resume), or of the whole run (<see cref="IsTerminal"/>). Codes only, never exception text. Plain settable properties:
    /// it is also the input of <c>NotifyChatErrorWorkflowActivity</c>.
    /// </summary>
    public class ChatAgentFailure
    {
        public string TenantName { get; set; }

        public string SessionId { get; set; }

        /// <summary>The run's id (workflow instance id, or the approval id of a resume): the UI shows one message per turn.</summary>
        public string TurnId { get; set; }

        /// <summary>When the run was requested. A Stop recorded after it suppresses the report.</summary>
        public DateTimeOffset? TurnStartedAt { get; set; }

        /// <summary>The run's own Stop flag, when the caller has it.</summary>
        public bool StopRequested { get; set; }

        /// <summary>One of <see cref="ChatErrorCodes"/>; anything else is sent as <see cref="ChatErrorCodes.ChatTurnFailed"/>.</summary>
        public string ErrorCode { get; set; }

        /// <summary>The failing agent (null for a turn-level failure). Its thinking bubble is stopped.</summary>
        public int? AgentId { get; set; }

        public string AgentName { get; set; }

        public string ChatbotIconUrl { get; set; }

        /// <summary>True: the run is over and nothing answered (the chat leaves "processing" with "error"). False: only this agent failed.</summary>
        public bool IsTerminal { get; set; }

        /// <summary>
        /// Non-terminal only: the run that failed was the last thing running in the chat (a hand-off, a sub-agent's resume),
        /// so the chat is set back to "idle" - unless a newer turn of the session already runs.
        /// </summary>
        public bool EnsureIdle { get; set; }

        /// <summary>Terminal only: every agent of the run, whose thinking bubbles are stopped.</summary>
        public List<ChatAgentRef> TypingAgents { get; set; } = [];

        /// <summary>Optional dedupe key (see <see cref="ChatTurnErrorNotification.DedupeKey"/>).</summary>
        public string DedupeKey { get; set; }

        /// <summary>
        /// The code sent for a failed agent: a known chat runtime code, and never a tool warning code (an agent whose run
        /// failed is an error, even when a tool was the cause - e.g. an MCP server it could not reach).
        /// </summary>
        public static string ToAgentErrorCode(string code)
        {
            var normalized = ChatErrorCodes.Normalize(code);
            return ChatErrorCodes.IsWarning(normalized) ? ChatErrorCodes.ChatTurnFailed : normalized;
        }
    }

    /// <summary>
    /// US 7959 / Task 7963: tells the chat that one agent of a multi-agent run - or the whole run - failed, through
    /// <see cref="IChatTurnErrorNotifier"/> (typing-off for the agent, the live failed-turn marker, "error" state when
    /// terminal; Stop-aware and deduped). The caller sends the agent's failed sub-agent card itself (it knows its action
    /// and orchestration id), only when this returns true. Never throws.
    /// </summary>
    public interface IChatAgentFailureReporter
    {
        /// <summary>True when the chat was notified (false: no chat, stopped by the user, or already reported).</summary>
        Task<bool> ReportAsync(ChatAgentFailure failure, CancellationToken cancellationToken = default);
    }

    public class ChatAgentFailureReporter : IChatAgentFailureReporter
    {
        private readonly IChatTurnErrorNotifier _notifier;
        private readonly IChatNotificationService _notificationService;
        private readonly IChatTurnRegistry _turnRegistry;
        private readonly ILogger<ChatAgentFailureReporter> _logger;

        public ChatAgentFailureReporter(IChatTurnErrorNotifier notifier, IChatNotificationService notificationService, IChatTurnRegistry turnRegistry,
            ILogger<ChatAgentFailureReporter> logger)
        {
            _notifier = notifier;
            _notificationService = notificationService;
            _turnRegistry = turnRegistry;
            _logger = logger;
        }

        public async Task<bool> ReportAsync(ChatAgentFailure failure, CancellationToken cancellationToken = default)
        {
            try
            {
                if (failure == null || string.IsNullOrWhiteSpace(failure.SessionId) || failure.StopRequested)
                    return false;

                var chatFailure = ChatTurnFailure.Create(ChatAgentFailure.ToAgentErrorCode(failure.ErrorCode), failure.AgentId, failure.AgentName, failure.ChatbotIconUrl,
                    isTerminal: failure.IsTerminal);

                var notified = await _notifier.NotifyAsync(new ChatTurnErrorNotification
                {
                    TenantName = failure.TenantName,
                    SessionId = failure.SessionId,
                    TurnId = failure.TurnId,
                    TurnStartedAt = failure.TurnStartedAt,
                    StopRequested = failure.StopRequested,
                    Failure = chatFailure,
                    DedupeKey = failure.DedupeKey,
                    TypingAgents = (failure.TypingAgents ?? []).Select(a => new ChatTurnAgent(a.AgentId, a.AgentName)).ToList()
                }, cancellationToken);

                if (notified && !failure.IsTerminal && failure.EnsureIdle && !NewerTurnRuns(failure))
                    await _notificationService.SendConversationStateUpdate(failure.SessionId, "idle");

                return notified;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reporting the failure {ErrorCode} of agent {AgentId} to session {SessionId} failed",
                    failure?.ErrorCode, failure?.AgentId, failure?.SessionId);
                return false;
            }
        }

        // A newer turn of the session (Stop then Send, or the next message) owns the chat's state: a late "idle" would hide it.
        private bool NewerTurnRuns(ChatAgentFailure failure) =>
            failure.TurnStartedAt is { } startedAt && _turnRegistry.HasRunningTurnStartedAfter(failure.TenantName, failure.SessionId, startedAt);
    }
}
