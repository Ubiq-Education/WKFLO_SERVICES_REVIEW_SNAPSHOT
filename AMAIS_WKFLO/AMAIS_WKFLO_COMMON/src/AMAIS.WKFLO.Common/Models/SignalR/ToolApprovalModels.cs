using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.SignalR
{
    /// <summary>
    /// WI 7575: payload of the <c>ToolApprovalRequest</c> SignalR event - an agent wants to run a tool function
    /// that needs the chatting person's approval. Also the base of the persisted approval card data
    /// (<see cref="ToolApprovalCardData"/>). JsonProperty names keep the Newtonsoft-serialized card camelCase;
    /// SignalR itself serializes camelCase too.
    /// </summary>
    public class ToolApprovalRequestUpdate
    {
        [JsonProperty("chatId")]
        public string ChatId { get; set; } = string.Empty;

        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [JsonProperty("approvalId")]
        public string ApprovalId { get; set; } = string.Empty;

        [JsonProperty("agentId")]
        public int AgentId { get; set; }

        [JsonProperty("agentName")]
        public string AgentName { get; set; } = string.Empty;

        [JsonProperty("chatbotIconUrl")]
        public string ChatbotIconUrl { get; set; }

        [JsonProperty("toolName")]
        public string ToolName { get; set; } = string.Empty;

        [JsonProperty("functionName")]
        public string FunctionName { get; set; } = string.Empty;

        [JsonProperty("functionDescription")]
        public string FunctionDescription { get; set; }

        /// <summary>
        /// WI 7726: admin-chosen friendly name of the MCP function, or null - display it instead of
        /// <see cref="FunctionName"/> (which stays the identifier) when set. Null on cards persisted before 7726.
        /// </summary>
        [JsonProperty("functionDisplayName")]
        public string FunctionDisplayName { get; set; }

        /// <summary>WI 7726: friendly description, or null - display it instead of <see cref="FunctionDescription"/> when set.</summary>
        [JsonProperty("functionDisplayDescription")]
        public string FunctionDisplayDescription { get; set; }

        /// <summary>The call's arguments as a JSON object string (shown under "Details").</summary>
        [JsonProperty("argumentsJson")]
        public string ArgumentsJson { get; set; } = "{}";

        [JsonProperty("expiresAtUtc")]
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    /// <summary>
    /// WI 7575: <c>data</c> of the persisted Forms-channel message with <c>formType: "ToolApproval"</c>, so a
    /// history reload re-renders the card in its current state.
    /// </summary>
    public class ToolApprovalCardData : ToolApprovalRequestUpdate
    {
        /// <summary>Pending | Approved | Denied | Expired.</summary>
        [JsonProperty("status")]
        public string Status { get; set; } = "Pending";

        /// <summary>AllowOnce | AllowForConversation | Deny | Expired; null while pending.</summary>
        [JsonProperty("decision", NullValueHandling = NullValueHandling.Ignore)]
        public string Decision { get; set; }
    }

    /// <summary>WI 7575: payload of the <c>ToolApprovalResolved</c> SignalR event.</summary>
    public class ToolApprovalResolvedUpdate
    {
        [JsonProperty("chatId")]
        public string ChatId { get; set; } = string.Empty;

        [JsonProperty("approvalId")]
        public string ApprovalId { get; set; } = string.Empty;

        /// <summary>AllowOnce | AllowForConversation | Deny | Expired.</summary>
        [JsonProperty("decision")]
        public string Decision { get; set; } = string.Empty;
    }
}
