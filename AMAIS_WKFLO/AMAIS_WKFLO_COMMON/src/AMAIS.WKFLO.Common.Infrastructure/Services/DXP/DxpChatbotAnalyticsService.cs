using AMAIS.DXP.INGRESS.Client.Lib.Api;
using AMAIS.DXP.INGRESS.Client.Lib.Models;
using AMAIS.WKFLO.Common.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Services.DXP
{
    public class DxpChatbotAnalyticsService : IDxpChatbotAnalyticsService
    {
        // DXP Ingress's plain InteractionCreate endpoint (CreateInteractionCommand / InteractionCreateAsync)
        // is built for browser-originated calls from the JS collector — it requires an Origin header matching
        // the tenant's configured Domain, which is naturally present on real browser requests but never present
        // on our server-to-server calls (a Dapr workflow activity, or the Worker's batch job). The
        // InteractionWithIPCreate endpoint (CreateInteractionWithIPCommand / InteractionWithIPCreateAsync) has
        // no such Origin/Domain check — it's the one actually meant for server-to-server calls — so we always
        // use it here, for every event, regardless of whether an HttpContext is present.
        //
        // UserAgent/Language are [Required] by DXP Ingress (DXPINGRESS0002 if empty) — real header value when
        // an HttpContext is genuinely available and meaningful, otherwise these placeholders identifying the
        // interaction as a server-side/background analytics signal. IPAddress has no such requirement, but for
        // consistency with how the Ingress endpoint's own code already falls back internally when there's no
        // real IP to report (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "None"), we use "None" too.
        private const string DefaultUserAgent = "AMAIS.WKFLO.Backend";
        private const string DefaultLanguage = "en";
        private const string NoIpAddress = "None";

        private readonly IInteractionApi _interactionApi;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<DxpChatbotAnalyticsService> _logger;

        public DxpChatbotAnalyticsService(
            IInteractionApi interactionApi,
            IHttpContextAccessor httpContextAccessor,
            ILogger<DxpChatbotAnalyticsService> logger)
        {
            _interactionApi = interactionApi;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public Task<bool> ReportConversationStartedAsync(DxpChatbotEventContext context, DateTimeOffset? conversationDate = null)
        {
            // Reuses the predicate the legacy in-house chatbot used for the same purpose, so a "conversation
            // started" event looks identical in DXP regardless of which chatbot generation produced it.
            var metadata = new Dictionary<string, string> { ["EventSchemaVersion"] = "2" };
            AddIfPresent(metadata, "ConversationDate", conversationDate);

            return SendAsync(context, predicate: "ChatBot Conversation", extraObjectMetadata: metadata);
        }

        public Task<bool> ReportConversationResolutionAsync(DxpChatbotEventContext context, bool isResolved, DateTimeOffset analyzedAt, DateTimeOffset? conversationDate = null)
        {
            var metadata = new Dictionary<string, string>
            {
                ["IsResolved"] = isResolved.ToString(),
                ["AnalyzedAt"] = analyzedAt.ToString("o"),
                ["EventSchemaVersion"] = "2"
            };
            AddIfPresent(metadata, "ConversationDate", conversationDate);

            return SendAsync(context, predicate: "Chatbot Conversation Resolution", extraObjectMetadata: metadata);
        }

        public Task<bool> ReportMessageFeedbackAsync(DxpChatbotEventContext context, string messageId, bool isThumbsUp,
            Guid? topicId = null, string topicName = null, DateTimeOffset? messageDate = null, DateTimeOffset? conversationDate = null,
            string topicVersion = null)
        {
            var metadata = new Dictionary<string, string>
            {
                ["MessageId"] = messageId ?? string.Empty,
                ["IsThumbsUp"] = isThumbsUp.ToString(),
                ["EventSchemaVersion"] = "2"
            };
            AddIfPresent(metadata, "TopicId", topicId);
            AddIfPresent(metadata, "TopicName", topicName);
            AddIfPresent(metadata, "MessageDate", messageDate);
            AddIfPresent(metadata, "ConversationDate", conversationDate);
            // US 7142 - same "omit rather than send empty" convention as every other optional field here.
            AddIfPresent(metadata, "TopicVersion", topicVersion);

            return SendAsync(context, predicate: "Chatbot Feedback", extraObjectMetadata: metadata);
        }

        public Task<bool> ReportMessageTopicAsync(DxpChatbotEventContext context, string messageId, Guid? topicId, string topicName, string topicVersion,
            DateTimeOffset? messageDate = null, DateTimeOffset? conversationDate = null,
            bool? isResolved = null, string question = null, string response = null, string answerMessageId = null)
        {
            var metadata = new Dictionary<string, string>
            {
                ["MessageId"] = messageId ?? string.Empty,
                ["EventSchemaVersion"] = "2"
            };
            // Exactly one call site reports this event per message (MessageResolutionWorkflowActivity /
            // ConversationAnalysisWorkflowActivity's per-message breakdown) - never a premature classification-
            // time report followed by a separate re-report, so every field here always has a real value except
            // TopicId/TopicName (a legitimate "Uncategorized" outcome) and TopicVersion (only if the tenant's
            // ChatClassification config was somehow unresolvable at judgment time). Still using the same
            // "omit rather than send empty" AddIfPresent convention as every optional field on this service,
            // since an ES keyword field can't tell "" apart from a real value on an exists/must_not-exists
            // filter (see this method's own doc comment above).
            AddIfPresent(metadata, "TopicVersion", topicVersion);
            AddIfPresent(metadata, "TopicId", topicId);
            AddIfPresent(metadata, "TopicName", topicName);
            AddIfPresent(metadata, "MessageDate", messageDate);
            AddIfPresent(metadata, "ConversationDate", conversationDate);
            AddIfPresent(metadata, "IsResolved", isResolved);
            AddIfPresent(metadata, "Question", question);
            AddIfPresent(metadata, "Response", response);
            AddIfPresent(metadata, "AnswerMessageId", answerMessageId);

            return SendAsync(context, predicate: "Chatbot Message Topic", extraObjectMetadata: metadata);
        }

        // Omits the key entirely rather than writing an empty string when the value is absent — same
        // convention SendAsync already uses for ShareId/TenantAgentId/PublishedTenantAgentId. This matters a
        // lot more for these fields than it looks: in Elasticsearch, an empty string IS a present value on a
        // keyword field (an `exists`/`must_not exists` filter can't tell "" apart from a real value), so writing
        // "" here would silently break DXP's Uncategorized detection (a must_not-exists filter on
        // TopicId.keyword). Worse, MessageDate/ConversationDate are brand-new fields in the data stream — if
        // the FIRST document ever written carries "", Elasticsearch's dynamic mapping locks them in as
        // text/keyword instead of date, permanently, for the whole data stream (no date_histogram/range query
        // could ever use them again without a full reindex).
        private static void AddIfPresent(Dictionary<string, string> metadata, string key, Guid? value)
        {
            if (value.HasValue)
                metadata[key] = value.Value.ToString();
        }

        private static void AddIfPresent(Dictionary<string, string> metadata, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
                metadata[key] = value;
        }

        private static void AddIfPresent(Dictionary<string, string> metadata, string key, bool? value)
        {
            if (value.HasValue)
                metadata[key] = value.Value.ToString();
        }

        private static void AddIfPresent(Dictionary<string, string> metadata, string key, DateTimeOffset? value)
        {
            if (value.HasValue)
                metadata[key] = value.Value.ToString("o");
        }

        private async Task<bool> SendAsync(
            DxpChatbotEventContext context,
            string predicate,
            Dictionary<string, string> extraObjectMetadata)
        {
            if (_interactionApi == null || context == null || string.IsNullOrEmpty(context.SessionId))
            {
                _logger.LogDebug("DXP chatbot analytics reporting skipped ({Predicate}) — not configured or missing session id", predicate);
                return false;
            }

            // Single choke point: test chats (CMS Test Agent tab, etc.) must never reach DXP, for any of the
            // four event types. Short-circuiting here — rather than at each ReportXAsync call site — keeps
            // that guarantee total regardless of which call site forgets to check.
            if (context.IsTestChat)
            {
                _logger.LogDebug("DXP chatbot analytics reporting skipped ({Predicate}) — conversation is a test chat, session {SessionId}", predicate, context.SessionId);
                return false;
            }

            // Subject carries the DXP contact identifier (ChatContext.SubjectId) for whoever is having the
            // conversation — without it there's no meaningful Subject to send, so skip entirely rather than
            // sending an empty one. Mirrors the guard style already used by DxpContactFormService.
            if (string.IsNullOrEmpty(context.SubjectId))
            {
                _logger.LogDebug("DXP chatbot analytics reporting skipped ({Predicate}) — missing subject id for session {SessionId}", predicate, context.SessionId);
                return false;
            }

            // Defensive completeness check: the resolved DxpIntegration row is expected to always carry a
            // non-empty DxpTenantName/Domain/ApiKey (all three are NOT NULL, non-empty-by-convention columns
            // set by the MD API's Create/Update endpoints) — but DXP Ingress rejects the call outright
            // (DXPINGRESS0104 "RequestDomainEmpty", DXPINGRESS0002 "Required" on other fields) if any of them
            // are empty, with no indication of which tenant/session triggered it. Failing loudly and early
            // here, with the identifying context, makes that failure mode diagnosable instead of only visible
            // as an opaque DXP-side error code.
            if (string.IsNullOrEmpty(context.DxpTenantName) || string.IsNullOrEmpty(context.Domain) || string.IsNullOrEmpty(context.ApiKey))
            {
                _logger.LogWarning(
                    "DXP chatbot analytics reporting skipped ({Predicate}) for tenant {TenantName} (WkfloTenantId={TenantId}), session {SessionId} — " +
                    "resolved DxpIntegration is missing DxpTenantName/Domain/ApiKey (DxpTenantNameEmpty={DxpTenantNameEmpty}, DomainEmpty={DomainEmpty}, ApiKeyEmpty={ApiKeyEmpty}). " +
                    "This usually means the wrong (or an incompletely configured) DxpIntegration row was resolved for this tenant.",
                    predicate, context.TenantName, context.TenantId, context.SessionId,
                    string.IsNullOrEmpty(context.DxpTenantName), string.IsNullOrEmpty(context.Domain), string.IsNullOrEmpty(context.ApiKey));
                return false;
            }

            try
            {
                var httpContext = _httpContextAccessor?.HttpContext;

                var objectMetadata = new CreateInteractionObjectMetadataCommand
                {
                    SiteName = context.DxpTenantName,
                    Metadata = new Dictionary<string, string>()
                };

                if (context.TenantAgentId.HasValue)
                    objectMetadata.Metadata["TenantAgentId"] = context.TenantAgentId.Value.ToString();

                if (context.PublishedTenantAgentId.HasValue)
                    objectMetadata.Metadata["PublishedTenantAgentId"] = context.PublishedTenantAgentId.Value.ToString();

                if (!string.IsNullOrWhiteSpace(context.ShareId))
                    objectMetadata.Metadata["ShareId"] = context.ShareId;

                if (extraObjectMetadata != null)
                {
                    foreach (var kvp in extraObjectMetadata)
                        objectMetadata.Metadata[kvp.Key] = kvp.Value;
                }

                var interaction = new CreateInteractionWithIPCommand
                {
                    Subject = context.SubjectId,
                    Predicate = predicate,
                    Object = context.SessionId,
                    Language = GetLanguage(httpContext),
                    Timestamp = DateTime.UtcNow,
                    IPAddress = GetIpAddress(httpContext),
                    UserAgent = GetUserAgent(httpContext),
                    SubjectMetadata = new Dictionary<string, string>(),
                    ObjectMetadata = objectMetadata
                };

                await _interactionApi.InteractionWithIPCreateAsync(interaction, context.DxpTenantName, context.Domain, context.ApiKey);

                _logger.LogInformation("DXP chatbot analytics event {Predicate} reported for session {SessionId}", predicate, context.SessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DXP: Error reporting chatbot analytics event {Predicate} for session {SessionId}", predicate, context.SessionId);
                return false;
            }
        }

        // Real IP when available; "None" (matching DXP Ingress's own internal fallback) when there's nothing
        // genuine to report — no HttpContext (the common case: a Dapr workflow activity or a Worker background
        // job), or no usable header/remote address on one that does exist.
        private static string GetIpAddress(HttpContext httpContext)
        {
            if (httpContext == null) return NoIpAddress;

            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor)) return forwardedFor.Split(',')[0].Trim();

            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
            return !string.IsNullOrEmpty(remoteIp) ? remoteIp : NoIpAddress;
        }

        // DXP Ingress marks UserAgent/Language as [Required] on every interaction command (DXPINGRESS0002 if
        // empty) — these always return a non-empty value, using the real HttpContext when one is genuinely
        // available and meaningful, and falling back to the fixed placeholders otherwise (the common case for
        // these 4 events, since most fire from a Dapr workflow activity or a Worker background job).
        private static string GetUserAgent(HttpContext httpContext)
        {
            var userAgent = httpContext?.Request.Headers["User-Agent"].FirstOrDefault();
            return !string.IsNullOrEmpty(userAgent) ? userAgent : DefaultUserAgent;
        }

        private static string GetLanguage(HttpContext httpContext)
        {
            var language = httpContext?.Request.Headers["Accept-Language"].FirstOrDefault();
            return !string.IsNullOrEmpty(language) ? language : DefaultLanguage;
        }
    }
}
