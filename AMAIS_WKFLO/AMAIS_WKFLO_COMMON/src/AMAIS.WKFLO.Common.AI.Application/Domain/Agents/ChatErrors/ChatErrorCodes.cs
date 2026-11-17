namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959: codes of a chat turn that failed after the send endpoint already returned (async runtime), pushed to the
    /// chat over SignalR. They live here, and not only in the AI API <c>ErrorConst</c>, because the classifier and the
    /// notifier in this project need them and this project cannot reference the AI API; <c>ErrorConst</c> exposes the
    /// same constants (it references these). The UI mirrors them in <c>shared/utils/chat-send-errors.ts</c>.
    /// </summary>
    public static class ChatErrorCodes
    {
        /// <summary>Unclassified fallback.</summary>
        public const string ChatTurnFailed = "WKFLOAI1101";

        /// <summary>408/504, a timeout or a cancellation that was not a user Stop.</summary>
        public const string ChatModelTimeout = "WKFLOAI1102";

        /// <summary>429 (not a quota problem).</summary>
        public const string ChatModelRateLimited = "WKFLOAI1103";

        /// <summary>429 insufficient_quota, 402, billing.</summary>
        public const string ChatModelQuotaExceeded = "WKFLOAI1104";

        /// <summary>401/403.</summary>
        public const string ChatModelAuthFailed = "WKFLOAI1105";

        /// <summary>The provider's content filter / safety system blocked the request or the answer.</summary>
        public const string ChatModelContentFiltered = "WKFLOAI1106";

        /// <summary>context_length_exceeded / prompt is too long / 413.</summary>
        public const string ChatModelContextTooLong = "WKFLOAI1107";

        /// <summary>5xx, 529 overloaded, network/DNS.</summary>
        public const string ChatModelUnavailable = "WKFLOAI1108";

        /// <summary>400/404 model or deployment not found, unsupported or missing provider.</summary>
        public const string ChatModelConfigInvalid = "WKFLOAI1109";

        /// <summary>The model answered with nothing and nothing is pending.</summary>
        public const string ChatModelEmptyResponse = "WKFLOAI1110";

        /// <summary>Worker-side validation failed, the agent is missing, inactive or not runnable (may carry a detail code).</summary>
        public const string ChatAgentUnavailable = "WKFLOAI1111";

        /// <summary>MCP not initialized, or connecting / listing its tools failed. Warning: the turn continues.</summary>
        public const string ChatToolUnavailable = "WKFLOAI1112";

        /// <summary>A tool threw. Warning: the turn continues.</summary>
        public const string ChatToolFailed = "WKFLOAI1113";

        /// <summary>Image upload or document parsing failed.</summary>
        public const string ChatAttachmentFailed = "WKFLOAI1114";

        /// <summary>Dapr state/pub-sub/workflow, database or chat history storage failed.</summary>
        public const string ChatInfrastructureError = "WKFLOAI1115";

        /// <summary>Multi-agent: the agents failed and no answer was produced.</summary>
        public const string ChatSubagentFailed = "WKFLOAI1116";

        /// <summary>Text-to-speech failed.</summary>
        public const string ChatVoiceSynthesisFailed = "WKFLOAI1117";

        /// <summary>Every chat runtime code. A code outside this set is never sent (it falls back to <see cref="ChatTurnFailed"/>).</summary>
        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        {
            ChatTurnFailed, ChatModelTimeout, ChatModelRateLimited, ChatModelQuotaExceeded, ChatModelAuthFailed,
            ChatModelContentFiltered, ChatModelContextTooLong, ChatModelUnavailable, ChatModelConfigInvalid,
            ChatModelEmptyResponse, ChatAgentUnavailable, ChatToolUnavailable, ChatToolFailed, ChatAttachmentFailed,
            ChatInfrastructureError, ChatSubagentFailed, ChatVoiceSynthesisFailed
        };

        /// <summary>
        /// The worker-side validation codes that may travel as <c>detailCode</c> next to <see cref="ChatAgentUnavailable"/>.
        /// They are already public (the send endpoints return them as 400s). Literal copies of the AI API
        /// <c>ErrorConst</c> values TenantInvalid, TenantMustExist, AgentMustExist, AgentNotFound and AgentNotRunnable
        /// (that project is not referenced from here); a unit test keeps both in sync.
        /// </summary>
        public static readonly IReadOnlySet<string> PublicDetailCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "WKFLOAI0101", "WKFLOAI0102", "WKFLOAI0302", "WKFLOAI0303", "WKFLOAI0305"
        };

        /// <summary>Tool problems are warnings: the turn goes on without that tool.</summary>
        public static bool IsWarning(string code) => code is ChatToolUnavailable or ChatToolFailed;

        /// <summary>Returns <paramref name="code"/> when it is a known chat runtime code, else <see cref="ChatTurnFailed"/>.</summary>
        public static string Normalize(string code) => code != null && All.Contains(code) ? code : ChatTurnFailed;

        /// <summary>The first of <paramref name="codes"/> allowed as a public detail code, or null.</summary>
        public static string ToPublicDetailCode(IEnumerable<string> codes) =>
            codes?.FirstOrDefault(c => c != null && PublicDetailCodes.Contains(c));

        /// <summary><paramref name="code"/> when it is allowed as a public detail code, or null.</summary>
        public static string ToPublicDetailCode(string code) => code != null && PublicDetailCodes.Contains(code) ? code : null;
    }

    /// <summary>US 7959: values of the <c>severity</c> metadata of a failed-turn marker.</summary>
    public static class ChatErrorSeverity
    {
        public const string Error = "error";
        public const string Warning = "warning";
    }
}
