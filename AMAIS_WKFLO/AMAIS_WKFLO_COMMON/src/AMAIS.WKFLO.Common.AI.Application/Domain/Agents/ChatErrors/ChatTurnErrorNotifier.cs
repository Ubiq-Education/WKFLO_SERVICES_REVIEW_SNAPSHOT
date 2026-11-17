using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models;
using Dapr;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors
{
    /// <summary>An agent whose thinking bubble a turn-level error must stop.</summary>
    public sealed record ChatTurnAgent(int? AgentId, string AgentName = null, string ChatbotIconUrl = null);

    /// <summary>US 7959: one failure to report to a chat (see <see cref="IChatTurnErrorNotifier"/>).</summary>
    public sealed record ChatTurnErrorNotification
    {
        /// <summary>Tenant of the turn: scopes the dedupe key and the stop state, like a Stop.</summary>
        public string TenantName { get; init; }

        /// <summary>The chat (SignalR group).</summary>
        public string SessionId { get; init; }

        /// <summary>Identifies the failed turn: the UI shows one message per turn, the marker id derives from it.</summary>
        public string TurnId { get; init; }

        /// <summary>When the turn was requested. A Stop recorded after this instant suppresses the notification.</summary>
        public DateTimeOffset? TurnStartedAt { get; init; }

        /// <summary>The turn's own Stop flag (<c>IChatTurn.StopRequested</c>), when the caller has the turn.</summary>
        public bool StopRequested { get; init; }

        public ChatTurnFailure Failure { get; init; }

        /// <summary>
        /// Optional dedupe key. By default one terminal error per turn (<see cref="TurnId"/>) and one warning per
        /// (turn, agent, code).
        /// </summary>
        public string DedupeKey { get; init; }

        /// <summary>Other agents of the turn that may still show a thinking bubble; stopped only for a terminal failure.</summary>
        public IReadOnlyList<ChatTurnAgent> TypingAgents { get; init; } = [];
    }

    /// <summary>
    /// US 7959: tells the chat a turn failed. In order: typing off for the failing agent (every known agent of the turn
    /// for a terminal failure), the live <see cref="FailedTurnMarker"/>, then <c>ConversationStateUpdate "error"</c> for a
    /// terminal failure. Suppressed when the user stopped the turn, and sent at most once per dedupe key across retries
    /// and replicas. Never throws.
    /// </summary>
    public interface IChatTurnErrorNotifier
    {
        /// <summary>
        /// Returns true when the chat was notified. Pass <see cref="CancellationToken.None"/> (or a short timeout), not the
        /// turn token: the turn token may already be cancelled by a timeout, and that failure still has to be reported.
        /// </summary>
        Task<bool> NotifyAsync(ChatTurnErrorNotification notification, CancellationToken cancellationToken = default);
    }

    public static class ChatTurnErrorConsts
    {
        /// <summary>Dapr state key prefix: <c>chat-error-{tenant}:{sessionId}:{dedupeKey}</c> marks a failure already reported.</summary>
        public const string StateKeyPrefix = "chat-error-";

        /// <summary>The key only has to outlive the retries of the same turn (Dapr activity retries, redeliveries).</summary>
        public const int StateTtlSeconds = 3600;

        /// <summary>ConversationStateUpdate state of a turn that ended with a terminal failure.</summary>
        public const string ErrorState = "error";

        public static string BuildStateKey(string tenantName, string sessionId, string dedupeKey) =>
            $"{StateKeyPrefix}{ChatStopConsts.BuildScope(tenantName, sessionId)}:{dedupeKey}";

        public static string BuildDefaultDedupeKey(string turnId, ChatTurnFailure failure) =>
            failure.IsTerminal ? turnId : $"{turnId}:{failure.AgentId}:{failure.Code}";
    }

    /// <summary>The value stored under the dedupe key; only its presence matters.</summary>
    public class ChatTurnErrorClaim
    {
        public DateTimeOffset ReportedAt { get; set; }
        public string ErrorCode { get; set; }
    }

    public class ChatTurnErrorNotifier : IChatTurnErrorNotifier
    {
        private readonly IChatNotificationService _notificationService;
        private readonly IChatStopStateStore _stopStateStore;
        private readonly DaprClient _daprClient;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<ChatTurnErrorNotifier> _logger;

        public ChatTurnErrorNotifier(IChatNotificationService notificationService, IChatStopStateStore stopStateStore, DaprClient daprClient,
            IDateTimeProvider dateTimeProvider, ILogger<ChatTurnErrorNotifier> logger)
        {
            _notificationService = notificationService;
            _stopStateStore = stopStateStore;
            _daprClient = daprClient;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        public async Task<bool> NotifyAsync(ChatTurnErrorNotification notification, CancellationToken cancellationToken = default)
        {
            try
            {
                if (notification?.Failure == null || string.IsNullOrWhiteSpace(notification.SessionId))
                    return false;

                // The dedupe key and the Stop state are scoped by (tenant, session): without a tenant neither can be trusted.
                if (string.IsNullOrWhiteSpace(notification.TenantName))
                {
                    _logger.LogWarning("Chat error {ErrorCode} of turn {TurnId} not reported to session {SessionId}: no tenant",
                        notification.Failure.Code, notification.TurnId, notification.SessionId);
                    return false;
                }

                var failure = notification.Failure;
                if (await IsStoppedAsync(notification, cancellationToken))
                {
                    _logger.LogInformation("Chat error {ErrorCode} of turn {TurnId} not reported: session {SessionId} was stopped by the user",
                        failure.Code, notification.TurnId, notification.SessionId);
                    return false;
                }

                var dedupeKey = string.IsNullOrWhiteSpace(notification.DedupeKey)
                    ? ChatTurnErrorConsts.BuildDefaultDedupeKey(notification.TurnId, failure)
                    : notification.DedupeKey;
                if (!await TryClaimAsync(notification, dedupeKey, failure, cancellationToken))
                {
                    _logger.LogInformation("Chat error {ErrorCode} of turn {TurnId} already reported to session {SessionId}",
                        failure.Code, notification.TurnId, notification.SessionId);
                    return false;
                }

                await SendAsync(notification, failure);

                _logger.LogInformation("Chat error {ErrorCode} ({Severity}, terminal: {IsTerminal}) reported to session {SessionId} for turn {TurnId}, agent {AgentId}",
                    failure.Code, failure.Severity, failure.IsTerminal, notification.SessionId, notification.TurnId, failure.AgentId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reporting a chat error to session {SessionId} failed", notification?.SessionId);
                return false;
            }
        }

        /// <summary>
        /// A user Stop is never an error: the turn's own flag, or a Stop recorded after the turn started (read once, right
        /// before sending, without throwing). A failed read does not suppress the error.
        /// </summary>
        private async Task<bool> IsStoppedAsync(ChatTurnErrorNotification notification, CancellationToken cancellationToken)
        {
            if (notification.StopRequested)
                return true;

            if (notification.TurnStartedAt is not { } startedAt)
                return false;

            try
            {
                var stoppedAt = await _stopStateStore.GetStoppedAtAsync(notification.TenantName, notification.SessionId, cancellationToken);
                return stoppedAt is { } value && value > startedAt;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading the stop state of session {SessionId} before reporting a chat error failed", notification.SessionId);
                return false;
            }
        }

        /// <summary>
        /// Insert-only write of the dedupe key (first-write concurrency, empty ETag, with a TTL), as the Stop state does.
        /// False when another attempt already wrote it. When the state store cannot be reached the error is still reported:
        /// a duplicate message is better than a chat stuck in "processing".
        /// </summary>
        private async Task<bool> TryClaimAsync(ChatTurnErrorNotification notification, string dedupeKey, ChatTurnFailure failure, CancellationToken cancellationToken)
        {
            var key = ChatTurnErrorConsts.BuildStateKey(notification.TenantName, notification.SessionId, dedupeKey);
            var metadata = new Dictionary<string, string> { ["ttlInSeconds"] = ChatTurnErrorConsts.StateTtlSeconds.ToString() };
            var claim = new ChatTurnErrorClaim { ReportedAt = _dateTimeProvider.DateTimeOffsetUtcNow(), ErrorCode = failure.Code };

            try
            {
                return await _daprClient.TrySaveStateAsync(
                    CommonAiPubSubConsts.StateStoreName,
                    key,
                    claim,
                    string.Empty,
                    new StateOptions { Concurrency = ConcurrencyMode.FirstWrite },
                    metadata,
                    cancellationToken);
            }
            catch (DaprException ex)
            {
                // Dapr reports an insert-only conflict as an error (the SDK throws instead of returning false): re-read to
                // tell a duplicate from an outage.
                try
                {
                    var existing = await _daprClient.GetStateAsync<ChatTurnErrorClaim>(CommonAiPubSubConsts.StateStoreName, key,
                        ConsistencyMode.Strong, cancellationToken: cancellationToken);
                    if (existing != null)
                        return false;
                }
                catch (Exception readEx)
                {
                    _logger.LogWarning(readEx, "Reading chat error dedupe key {Key} failed", key);
                }

                _logger.LogWarning(ex, "Writing chat error dedupe key {Key} failed; reporting the error anyway", key);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Writing chat error dedupe key {Key} failed; reporting the error anyway", key);
                return true;
            }
        }

        /// <summary>Each send is independent: a failed one is logged and the next one is still sent.</summary>
        private async Task SendAsync(ChatTurnErrorNotification notification, ChatTurnFailure failure)
        {
            var sessionId = notification.SessionId;

            // 1. Stop the thinking bubbles: the failing agent's, and every known agent's when the turn is over.
            foreach (var agent in TypingAgentsToStop(notification, failure))
                await TrySendAsync(() => _notificationService.SendTypingIndicator(sessionId, false, agent.AgentId, agent.AgentName, agent.ChatbotIconUrl),
                    "typing indicator", sessionId);

            // 2. The live marker (never persisted).
            var marker = FailedTurnMarker.BuildLiveMessage(notification.TurnId, failure, _dateTimeProvider.DateTimeOffsetUtcNow());
            await TrySendAsync(() => _notificationService.SendMessageToChat(sessionId, marker, failure.AgentId, failure.AgentName, failure.ChatbotIconUrl),
                "failed-turn marker", sessionId);

            // 3. Terminal only: the conversation leaves "processing". Codes only, no free text.
            if (failure.IsTerminal)
            {
                var metadata = new Dictionary<string, object>
                {
                    [CommonMetadataConsts.ErrorCode] = ChatErrorCodes.Normalize(failure.Code),
                    [CommonMetadataConsts.TurnId] = notification.TurnId ?? string.Empty
                };
                await TrySendAsync(() => _notificationService.SendConversationStateUpdate(sessionId, ChatTurnErrorConsts.ErrorState, metadata),
                    "conversation state", sessionId);
            }
        }

        private static IEnumerable<ChatTurnAgent> TypingAgentsToStop(ChatTurnErrorNotification notification, ChatTurnFailure failure)
        {
            var agents = new List<ChatTurnAgent> { new(failure.AgentId, failure.AgentName, failure.ChatbotIconUrl) };
            if (failure.IsTerminal && notification.TypingAgents != null)
                agents.AddRange(notification.TypingAgents.Where(a => a != null));

            return agents.DistinctBy(a => a.AgentId);
        }

        private async Task TrySendAsync(Func<Task> send, string what, string sessionId)
        {
            try
            {
                await send();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sending the {What} of a chat error to session {SessionId} failed", what, sessionId);
            }
        }
    }
}
