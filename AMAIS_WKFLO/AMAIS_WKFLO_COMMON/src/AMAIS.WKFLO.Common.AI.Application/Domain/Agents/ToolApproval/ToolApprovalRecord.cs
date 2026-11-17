using System.Text.Json.Serialization;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval
{
    /// <summary>WI 7575: fixed values of the "Ask for permission" flow.</summary>
    public static class ToolApprovalConsts
    {
        /// <summary><c>formType</c> of the persisted Forms-channel approval card.</summary>
        public const string FormType = "ToolApproval";

        /// <summary>An unanswered request expires (= denied) after this long.</summary>
        public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

        public static class Status
        {
            public const string Pending = "Pending";
            public const string Approved = "Approved";
            public const string Denied = "Denied";
            public const string Expired = "Expired";
        }

        public static class Decision
        {
            public const string AllowOnce = "AllowOnce";
            public const string AllowForConversation = "AllowForConversation";
            public const string Deny = "Deny";
            public const string Expired = "Expired";

            /// <summary>The decisions a person can send (Expired is only ever set by the server).</summary>
            public static bool IsUserDecision(string value) => value is AllowOnce or AllowForConversation or Deny;

            public static string ToStatus(string decision) => decision switch
            {
                AllowOnce or AllowForConversation => Status.Approved,
                Deny => Status.Denied,
                _ => Status.Expired
            };
        }
    }

    /// <summary>
    /// WI 7575: one pending (or decided) tool call that needs approval. Stored in the Dapr state store (same
    /// multi-instance-safe store as the ContactForm replay state) so any AI API instance can decide/resume it.
    /// Holds everything needed to run the STORED call later without asking the model again.
    /// </summary>
    public class ToolApprovalRecord
    {
        public string ApprovalId { get; set; }
        public string TenantName { get; set; }

        /// <summary>The chatting user (UserContext.Guid) - only that user may decide. Null/empty for anonymous chats.</summary>
        public string UserId { get; set; }

        public string SessionId { get; set; }

        /// <summary>The runnable agent that ran the turn (TenantAgent for the admin test chat, PublishedTenantAgent for a shared chat).</summary>
        public int AgentId { get; set; }
        public string AgentName { get; set; }

        /// <summary>Share id of the public chat; null for the authenticated tenant chat.</summary>
        public string ShareId { get; set; }

        /// <summary>
        /// Bug 7722: set when the request came from a sub-agent hand-off - the agent whose chat it is (the root). The
        /// root's chat may decide it, and the resume checks the sub-agent is still on the root's spin-up allowlist.
        /// Null when the agent that asked is the chat's own agent.
        /// </summary>
        public int? RootAgentId { get; set; }

        public string ChatbotIconUrl { get; set; }

        /// <summary>
        /// The original turn's chat context (page/feature/subject headers, widget flag), restored on resume so the
        /// resumed turn behaves like the turn that asked - independent of the decision request's own headers.
        /// </summary>
        public ToolApprovalChatState Chat { get; set; } = new();

        public int ToolId { get; set; }
        public string ToolName { get; set; }
        public bool IsMcp { get; set; }
        public string PluginName { get; set; }
        public string FunctionName { get; set; }
        public string FunctionDescription { get; set; }

        /// <summary>WI 7726: the MCP function's friendly display name when the run's tool copy has one (display only).</summary>
        public string FunctionDisplayName { get; set; }

        /// <summary>WI 7726: the MCP function's friendly display description when set (display only).</summary>
        public string FunctionDisplayDescription { get; set; }

        /// <summary>The model's arguments for the call, as a JSON object.</summary>
        public string ArgumentsJson { get; set; }

        /// <summary>The model's tool call id; its placeholder result in chat history is replaced on resume.</summary>
        public string CallId { get; set; }

        /// <summary>MessageId of the user message whose turn asked for approval (reused by the resume turn).</summary>
        public Guid? UserMessageId { get; set; }

        /// <summary>MessageId of the persisted approval card.</summary>
        public string CardMessageId { get; set; }

        public string Status { get; set; } = ToolApprovalConsts.Status.Pending;
        public string Decision { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }

        [JsonIgnore]
        public bool IsPending => Status == ToolApprovalConsts.Status.Pending;

        /// <summary>Key of the "Allow for this conversation" grant this call would match (tool, plus function for MCP).</summary>
        [JsonIgnore]
        public string GrantKey => ToolApprovalGrantKey.For(ToolId, IsMcp ? FunctionName : null);
    }

    /// <summary>WI 7575: the parts of the original turn's chat context a resume must reproduce.</summary>
    public class ToolApprovalChatState
    {
        public string PagePath { get; set; }

        /// <summary>Task 7749: CMS ID of the current page, so DXP page analytics keeps identifying it by ID on a resume.</summary>
        public string PageId { get; set; }
        public string TimeZone { get; set; }
        public string SubjectId { get; set; }
        public string FeatureId { get; set; }
        public string FeatureType { get; set; }
        public string FeatureName { get; set; }
        public List<string> AmaisAvailableEndpoints { get; set; }
        public bool IsTestChat { get; set; }

        /// <summary>The run's RootIsWidget (picks the chat setting/icon); null for a root agent's own chat.</summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>Bug 7722: see <see cref="Contexts.ChatContext.SubagentActivityAction"/>.</summary>
        public string SubagentActivityAction { get; set; }

        public static ToolApprovalChatState From(Contexts.ChatContext chatContext, bool? rootIsWidget) => new()
        {
            PagePath = chatContext?.PagePath,
            PageId = chatContext?.PageId,
            TimeZone = chatContext?.TimeZone,
            SubjectId = chatContext?.SubjectId,
            FeatureId = chatContext?.FeatureId,
            FeatureType = chatContext?.FeatureType,
            FeatureName = chatContext?.FeatureName,
            AmaisAvailableEndpoints = chatContext?.AmaisAvailableEndpoints?.ToList(),
            IsTestChat = chatContext?.IsTestChat ?? false,
            RootIsWidget = rootIsWidget,
            SubagentActivityAction = chatContext?.SubagentActivityAction
        };

        /// <summary>A fresh chat context for the resume turn of <paramref name="sessionId"/>.</summary>
        public Contexts.ChatContext ToChatContext(string sessionId) => new()
        {
            SessionId = sessionId,
            PagePath = PagePath,
            PageId = PageId,
            TimeZone = TimeZone,
            SubjectId = SubjectId,
            FeatureId = FeatureId,
            FeatureType = FeatureType,
            FeatureName = FeatureName,
            AmaisAvailableEndpoints = AmaisAvailableEndpoints,
            IsTestChat = IsTestChat,
            SubagentActivityAction = SubagentActivityAction
        };
    }

    /// <summary>
    /// WI 7575: who may decide an approval. An approval requested by a signed-in user can only be decided by that same
    /// user; an anonymous one (no user id, or the empty GUID) only by an anonymous caller. In both cases the caller must
    /// also be in the same session (checked separately).
    /// </summary>
    public static class ToolApprovalIdentity
    {
        public static string Normalize(string userId) =>
            string.IsNullOrWhiteSpace(userId) || (Guid.TryParse(userId, out var guid) && guid == Guid.Empty)
                ? null
                : userId.Trim();

        public static bool IsSameUser(string recordUserId, string callerUserId) =>
            string.Equals(Normalize(recordUserId), Normalize(callerUserId), StringComparison.OrdinalIgnoreCase);
    }

    public static class ToolApprovalGrantKey
    {
        /// <summary>Grant scope = session + tool (+ function for MCP).</summary>
        public static string For(int toolId, string mcpFunctionName) =>
            string.IsNullOrEmpty(mcpFunctionName) ? toolId.ToString() : $"{toolId}:{mcpFunctionName}";
    }
}
