using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.SignalR
{
    public class ChatMessageRequest
    {
        public string Input { get; set; } = string.Empty;
        public string SessionId { get; set; }
    }

    public class MessageUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public ChatMessage Message { get; set; } = new();
        public int? AgentId { get; set; }
        public string AgentName { get; set; }
        public string ChatbotIconUrl { get; set; }
    }

    public class ChatMessage
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "user", "assistant", "tool", "system"
        public string Content { get; set; } = string.Empty;
        public string FileUrl { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public ChannelMessageType ChannelMessageType { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public List<ContentItem> ContentItems { get; set; }
    }

    public class ContentItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; }
        public string Status { get; set; } = string.Empty; // "success", "failed"
        public int? StatusCode { get; set; }
    }

    public class ToolExecutionUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "started", "completed", "failed"
        public string Description { get; set; }
        public int? StatusCode { get; set; }
        public string Error { get; set; }

        /// <summary>US 7959: chat runtime code (WKFLOAI11xx) of a "failed" update; additive, older clients ignore it.</summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// US 7974: UTC instant (ISO 8601 with offset) the server sent this update; additive, older clients ignore it.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>
    /// Pushed when a subagent starts/finishes doing work as part of either subagent-invocation path —
    /// the LLM-triggered spin-up hand-off (<c>AgentEventPublisherPlugin</c> / <c>AgentEventWorkflowActivity</c>)
    /// or the Dapr multi-agent orchestrator workflow (<c>InvokeAgentWorkflowActivity</c>) — so the chat UI
    /// can render a persistent timeline card for the subagent's activity (task #7297), following the same
    /// started/completed/failed lifecycle as <see cref="ToolExecutionUpdate"/>.
    /// </summary>
    public class SubagentActivityUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public int? AgentId { get; set; }
        public string AgentName { get; set; }
        public string ChatbotIconUrl { get; set; }
        public string Action { get; set; }
        public string Status { get; set; } = string.Empty; // "started", "completed", "failed"
        public string OrchestrationInstanceId { get; set; }
        public string Error { get; set; }

        /// <summary>US 7959: chat runtime code (WKFLOAI11xx) of a "failed" update; additive, older clients ignore it.</summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// US 7974: UTC instant (ISO 8601 with offset) the server sent this update; additive, older clients ignore it.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }
    }

    public class TypingIndicatorUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public bool IsTyping { get; set; }
        public int? AgentId { get; set; }
        public string AgentName { get; set; }
        public string ChatbotIconUrl { get; set; }
    }

    public class StreamingTokenUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string[] Tokens { get; set; } = [];
        public bool IsComplete { get; set; }
        public ChannelMessageType ChannelMessageType { get; set; }
        public int? AgentId { get; set; }
        public string AgentName { get; set; }
        public string ChatbotIconUrl { get; set; }

        /// <summary>
        /// US 7974: UTC instant (ISO 8601 with offset) of the message these tokens belong to. Taken once when the
        /// message starts, so every update of the same <see cref="MessageId"/> carries the same value, which is also
        /// the timestamp persisted for that message and returned by the chat history. Additive, older clients ignore it.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }
    }

    public class ConversationStateUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty; // "active", "processing", "idle", "error"
        public Dictionary<string, object> Metadata { get; set; }
    }

    /// <summary>
    /// US 7501: payload of the <c>ConversationStopped</c> hub event, sent as soon as the user presses Stop.
    /// </summary>
    public class ConversationStoppedUpdate
    {
        public string ChatId { get; set; } = string.Empty;

        /// <summary>UTC instant of the Stop; every turn of the chat requested before it is cancelled.</summary>
        public DateTimeOffset StoppedAt { get; set; }
    }

    /// <summary>
    /// US 7507: payload of the <c>ConversationTitleUpdated</c> hub event, sent once the conversation's AI title is saved.
    /// </summary>
    public class ConversationTitleUpdate
    {
        /// <summary>The chat session id (the hub group the update is sent to).</summary>
        public string ChatId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
    }

    public class AudioChunkUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public string Base64Audio { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool IsFinal { get; set; }
    }

    public class AudioResponseCompleteUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public string FullText { get; set; } = string.Empty;
    }

    public class ContactFormCompletedUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public string OriginalMessage { get; set; } = string.Empty;
    }

    public class ContactFormUpdate
    {
        public string ChatId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool Mandatory { get; set; }
        public bool FirstNameRequired { get; set; }
        public bool LastNameRequired { get; set; }
        public bool EmailRequired { get; set; }
        public string SubmitButtonText { get; set; } = "Submit";
        public bool SuccessMessageEnabled { get; set; }
        public string SuccessMessageText { get; set; }
    }

    public class TextSuggestionUpdate
    {
        // No ChatId here — this is now serialized into the CommonMetadataConsts.Data metadata
        // key of a normal ChatMessageContent routed through the standard chat pipeline
        // (SemanticKernelAgentService.ProcessAssistantPostMessagesAsync), which is already
        // scoped to the session. It's no longer pushed over a dedicated side-channel that
        // needed its own ChatId for filtering.
        //
        // [JsonProperty] is required here: JsonConvert.SerializeObject writes exact C# PascalCase
        // property names with no naming policy, but the frontend's hydrateFormsMetadata()/
        // TextSuggestionCard expect camelCase to match what the UI saves (same rule as
        // "Adding a New Tool Type" step 2 in CLAUDE.md for {Name}ToolConfig classes).
        [JsonProperty("featureId")]
        public int FeatureId { get; set; }
        [JsonProperty("featureName")]
        public string FeatureName { get; set; } = string.Empty;
        [JsonProperty("suggestedText")]
        public string SuggestedText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Payload for the <c>ChannelMessageType.Forms</c> "McpOAuthReconnect" form type (see
    /// <c>SemanticKernelExtensions.SendMcpOAuthReconnectNotificationAsync</c>) — renders a button on the
    /// wkflo_ui side whose click fetches <see cref="StartPath"/> with the widget's own auth headers, then
    /// opens the returned <c>authorizationUrl</c>. <see cref="StartPath"/> is deliberately relative (no
    /// host/base URL): the frontend already prepends its own configured API base to every relative path it
    /// calls (see <c>public-api-service.ts</c>), so the backend never needs to know its own public base URL.
    /// </summary>
    public class McpOAuthReconnectUpdate
    {
        // [JsonProperty] is required here: JsonConvert.SerializeObject writes exact C# PascalCase
        // property names with no naming policy, but the frontend's hydrateFormsMetadata()/
        // McpOAuthReconnectCard expect camelCase to match what the UI saves (same rule as
        // TextSuggestionUpdate above).
        [JsonProperty("toolName")]
        public string ToolName { get; set; } = string.Empty;
        [JsonProperty("startPath")]
        public string StartPath { get; set; } = string.Empty;
        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }
}
