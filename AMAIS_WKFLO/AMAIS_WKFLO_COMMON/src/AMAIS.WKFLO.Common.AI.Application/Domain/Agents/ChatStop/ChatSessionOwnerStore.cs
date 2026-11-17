using AMAIS.WKFLO.Common.Models;
using Dapr;
using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop
{
    /// <summary>
    /// US 7501: who started a chat session: the chat (its agent, or its share id for a public chat) and the user. Only
    /// needed until the session has conversation rows, which then tell who owns it (see the Stop validators).
    /// </summary>
    public sealed class ChatSessionOwner
    {
        public int? AgentId { get; set; }

        public string ShareId { get; set; }

        /// <summary>The chatting user (UserContext.Guid); null, empty or Guid.Empty for an anonymous chat.</summary>
        public string UserId { get; set; }
    }

    /// <summary>
    /// US 7501: the owner of a session that has no conversation row yet, kept in the AI state store under
    /// <c>chat-session-owner-{tenant}:{sessionId}</c> so every replica sees it. The first message of a session records it;
    /// later messages never replace it.
    /// </summary>
    public interface IChatSessionOwnerStore
    {
        /// <summary>Records the owner unless the session already has one. Best effort: never throws (logs a warning).</summary>
        Task RecordAsync(string tenantName, string sessionId, ChatSessionOwner owner, CancellationToken cancellationToken = default);

        /// <summary>The recorded owner, or null when none. Throws when the state store cannot be read.</summary>
        Task<ChatSessionOwner> GetAsync(string tenantName, string sessionId, CancellationToken cancellationToken = default);
    }

    public class ChatSessionOwnerStore : IChatSessionOwnerStore
    {
        public const string StateKeyPrefix = "chat-session-owner-";

        /// <summary>A first turn is long finished (and the session has conversation rows) well within a day.</summary>
        public const int StateTtlSeconds = 24 * 3600;

        private readonly DaprClient _daprClient;
        private readonly ILogger<ChatSessionOwnerStore> _logger;

        public ChatSessionOwnerStore(DaprClient daprClient, ILogger<ChatSessionOwnerStore> logger)
        {
            _daprClient = daprClient;
            _logger = logger;
        }

        public static string BuildStateKey(string tenantName, string sessionId) => $"{StateKeyPrefix}{ChatStopConsts.BuildScope(tenantName, sessionId)}";

        public async Task RecordAsync(string tenantName, string sessionId, ChatSessionOwner owner, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(sessionId) || owner == null)
                return;

            var key = BuildStateKey(tenantName, sessionId);
            try
            {
                // Insert-only (first-write concurrency, no ETag): one round trip for the first message, and a session's
                // owner is never replaced.
                await _daprClient.TrySaveStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName,
                    key,
                    owner,
                    string.Empty,
                    new StateOptions { Concurrency = ConcurrencyMode.FirstWrite },
                    new Dictionary<string, string> { ["ttlInSeconds"] = StateTtlSeconds.ToString() },
                    cancellationToken);
            }
            catch (DaprException ex)
            {
                // Bug 8222: Dapr reports the insert-only write finding the key as an error (the SDK throws instead of
                // returning false), and its wording depends on the store: the Redis store sends "failed to set key ...
                // user_script", with no "etag" in it. Re-read to tell every message after the first from an outage.
                var (recorded, readError) = await ReadBackAsync(key, cancellationToken);
                if (recorded)
                    return;

                // The message itself must still go through; only a Stop of this session's first turn is then refused.
                if (readError == null)
                    _logger.LogWarning(ex, "Recording the owner of session {SessionId} failed", sessionId);
                else
                    _logger.LogWarning(ex, "Recording the owner of session {SessionId} failed; reading it back also failed: {ReadError}",
                        sessionId, readError.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Recording the owner of session {SessionId} failed", sessionId);
            }
        }

        /// <summary>Whether the session already has a recorded owner, and the read's own error when it failed too.</summary>
        private async Task<(bool Recorded, Exception ReadError)> ReadBackAsync(string key, CancellationToken cancellationToken)
        {
            try
            {
                var existing = await _daprClient.GetStateAsync<ChatSessionOwner>(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, key, ConsistencyMode.Strong, cancellationToken: cancellationToken);
                return (existing != null, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (false, ex);
            }
        }

        public Task<ChatSessionOwner> GetAsync(string tenantName, string sessionId, CancellationToken cancellationToken = default) =>
            _daprClient.GetStateAsync<ChatSessionOwner>(
                CommonConsts.CommonAiPubSubConsts.StateStoreName,
                BuildStateKey(tenantName, sessionId),
                cancellationToken: cancellationToken);
    }
}
