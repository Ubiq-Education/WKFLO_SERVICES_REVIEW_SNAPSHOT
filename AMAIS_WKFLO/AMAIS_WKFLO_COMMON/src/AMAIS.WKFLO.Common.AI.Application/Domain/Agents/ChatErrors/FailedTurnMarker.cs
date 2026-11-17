using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959: the message a failed turn (or a failed tool) shows in the chat. Sibling of the Stopped marker
    /// (<c>StoppedTurnMarker</c>) but live only: it is sent over SignalR <c>ReceiveMessage</c> and never saved to the chat
    /// history. The UI renders it from <see cref="CommonMetadataConsts.ErrorCode"/>; <see cref="ChatMessage.Content"/> is a
    /// neutral English fallback for clients that do not know the codes. It never carries exception text.
    /// </summary>
    public static class FailedTurnMarker
    {
        public const string ErrorText = "Sorry, something went wrong while answering. Please try again.";

        public const string ToolWarningText = "A tool this assistant uses isn't available right now.";

        /// <summary>
        /// The marker as sent live. Its id is derived from the turn (and agent, and code for a warning), so a resend of
        /// the same failure has the same id and the UI can show it once.
        /// </summary>
        public static ChatMessage BuildLiveMessage(string turnId, ChatTurnFailure failure, DateTimeOffset failedAt)
        {
            ArgumentNullException.ThrowIfNull(failure);

            return new ChatMessage
            {
                Id = BuildMessageId(turnId, failure),
                Type = "assistant",
                Content = IsWarning(failure) ? ToolWarningText : ErrorText,
                ChannelMessageType = ChannelMessageType.Metadata,
                Timestamp = failedAt,
                Metadata = BuildMetadata(turnId, failure, failedAt)
            };
        }

        /// <summary>The marker keys exposed to the chat UI, as plain values. Optional keys are left out when empty.</summary>
        public static Dictionary<string, object> BuildMetadata(string turnId, ChatTurnFailure failure, DateTimeOffset failedAt)
        {
            ArgumentNullException.ThrowIfNull(failure);

            var warning = IsWarning(failure);
            var metadata = new Dictionary<string, object>
            {
                [CommonMetadataConsts.RenderAsSystem] = true,
                [CommonMetadataConsts.IsError] = true,
                // Allowlists re-applied here too, so a hand-built failure can never put free text on the wire.
                [CommonMetadataConsts.ErrorCode] = ChatErrorCodes.Normalize(failure.Code),
                [CommonMetadataConsts.Severity] = warning ? ChatErrorSeverity.Warning : ChatErrorSeverity.Error,
                [CommonMetadataConsts.IsTerminal] = failure.IsTerminal,
                [CommonMetadataConsts.TurnId] = turnId ?? string.Empty,
                [CommonMetadataConsts.FailedAt] = FormatFailedAt(failedAt)
            };

            if (ChatErrorCodes.ToPublicDetailCode(failure.DetailCode) is { } detailCode)
                metadata[CommonMetadataConsts.DetailCode] = detailCode;
            if (failure.AgentId.HasValue)
                metadata[CommonMetadataConsts.AgentId] = failure.AgentId.Value;
            if (!string.IsNullOrWhiteSpace(failure.AgentName))
                metadata[CommonMetadataConsts.AgentName] = failure.AgentName;
            if (!string.IsNullOrWhiteSpace(failure.ChatbotIconUrl))
                metadata[CommonMetadataConsts.ChatbotIconUrl] = failure.ChatbotIconUrl;

            return metadata;
        }

        /// <summary>
        /// Deterministic GUID: one per (turn, agent) for an error and one per (turn, agent, code) for a warning, so the
        /// same failure always gets the same id while a tool warning and the turn error of the same turn stay distinct.
        /// </summary>
        public static string BuildMessageId(string turnId, ChatTurnFailure failure)
        {
            ArgumentNullException.ThrowIfNull(failure);

            var agent = failure.AgentId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            var seed = IsWarning(failure)
                ? $"chat-error:warning:{turnId}:{agent}:{ChatErrorCodes.Normalize(failure.Code)}"
                : $"chat-error:error:{turnId}:{agent}";

            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes(seed), hash);
            var bytes = hash[..16];

            // RFC 4122 name-based layout: version 5 and the variant bits, so it reads as a regular GUID.
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes, bigEndian: true).ToString();
        }

        public static string FormatFailedAt(DateTimeOffset failedAt) =>
            failedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        private static bool IsWarning(ChatTurnFailure failure) => failure.Severity == ChatErrorSeverity.Warning;
    }
}
