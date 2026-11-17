namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959: what the chat is told about a failed turn (or a failed tool): a code, never exception text. Built with
    /// <see cref="Create"/>, which keeps the code and the detail code inside their allowlists.
    /// </summary>
    public sealed record ChatTurnFailure
    {
        /// <summary>One of <see cref="ChatErrorCodes"/>.</summary>
        public string Code { get; init; } = ChatErrorCodes.ChatTurnFailed;

        /// <summary>Optional, already-public validation code (see <see cref="ChatErrorCodes.PublicDetailCodes"/>).</summary>
        public string DetailCode { get; init; }

        /// <summary><see cref="ChatErrorSeverity.Error"/> or <see cref="ChatErrorSeverity.Warning"/>.</summary>
        public string Severity { get; init; } = ChatErrorSeverity.Error;

        /// <summary>True when the turn ended because of this failure; false for a warning (the turn goes on).</summary>
        public bool IsTerminal { get; init; } = true;

        /// <summary>The agent that failed, when known. Its thinking bubble is stopped.</summary>
        public int? AgentId { get; init; }

        public string AgentName { get; init; }

        public string ChatbotIconUrl { get; init; }

        /// <summary>
        /// Tool problems (<see cref="ChatErrorCodes.IsWarning"/>) are non-terminal warnings, everything else is a terminal
        /// error unless <paramref name="isTerminal"/> says otherwise. An unknown code becomes
        /// <see cref="ChatErrorCodes.ChatTurnFailed"/>; a detail code outside the allowlist is dropped.
        /// </summary>
        public static ChatTurnFailure Create(string code, int? agentId = null, string agentName = null, string chatbotIconUrl = null,
            string detailCode = null, bool? isTerminal = null)
        {
            var normalized = ChatErrorCodes.Normalize(code);
            var warning = ChatErrorCodes.IsWarning(normalized);
            return new ChatTurnFailure
            {
                Code = normalized,
                DetailCode = ChatErrorCodes.ToPublicDetailCode(detailCode),
                Severity = warning ? ChatErrorSeverity.Warning : ChatErrorSeverity.Error,
                IsTerminal = isTerminal ?? !warning,
                AgentId = agentId,
                AgentName = agentName,
                ChatbotIconUrl = chatbotIconUrl
            };
        }
    }
}
