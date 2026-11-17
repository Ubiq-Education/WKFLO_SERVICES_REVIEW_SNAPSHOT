using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval
{
    /// <summary>
    /// WI 7575: pure chat-history operations of the approval flow (approval card, placeholder tool result,
    /// dangling tool calls). Kept free of I/O so it is directly unit-testable.
    /// </summary>
    public static class ToolApprovalHistory
    {
        /// <summary>Result the model sees for a call that is waiting for approval.</summary>
        public const string AwaitingApprovalResult =
            "[PENDING APPROVAL] This tool call needs the user's approval before it can run, and an approval request has been shown to them. "
            + "Do not call any other tool now. Briefly tell the user you need their permission to continue.";

        /// <summary>Result for a call the model made in the same batch after the one that is waiting for approval.</summary>
        public const string NotRunResult = "[NOT RUN] This tool call was not run because another call in the same step is waiting for the user's approval.";

        public const string DeniedResult = "[DENIED] The user did not allow this tool call, so it was not run. Do not try it again for this request; continue without it.";

        public const string ExpiredResult = "[EXPIRED] The user did not answer the approval request in time, so this tool call was not run.";

        public const string NoLongerAvailableResult = "[NOT RUN] The agent or its shared chat is no longer available, so the approved call was not run.";

        public const string UnavailableResult = "[NOT RUN] This tool is no longer available, so the approved call could not be run.";

        /// <summary>
        /// Result for an approved call that threw. The exception is logged, never put in the chat history: its message can
        /// carry internal details (URLs, hosts, SDK errors) the model could repeat to the user.
        /// </summary>
        public const string FailedResult = "[ERROR] The approved tool call failed. Tell the user it could not be completed and do not retry it for this request.";

        /// <summary>Bug 7722: result for the approved call repeated with the same arguments in its resumed turn.</summary>
        public const string AlreadyDoneResult = "[ALREADY DONE] This exact call was already run with the user's approval. Do not call it again; use its earlier result.";

        public static ToolApprovalCardData BuildCard(ToolApprovalRecord record) => new()
        {
            ChatId = record.SessionId,
            SessionId = record.SessionId,
            ApprovalId = record.ApprovalId,
            AgentId = record.AgentId,
            AgentName = record.AgentName,
            ChatbotIconUrl = record.ChatbotIconUrl,
            ToolName = record.ToolName,
            FunctionName = record.FunctionName,
            FunctionDescription = record.FunctionDescription,
            FunctionDisplayName = record.FunctionDisplayName,
            FunctionDisplayDescription = record.FunctionDisplayDescription,
            ArgumentsJson = string.IsNullOrWhiteSpace(record.ArgumentsJson) ? "{}" : record.ArgumentsJson,
            ExpiresAtUtc = record.ExpiresAt,
            Status = record.Status,
            Decision = record.Decision
        };

        public static ToolApprovalRequestUpdate BuildRequestUpdate(ToolApprovalRecord record)
        {
            var card = BuildCard(record);
            return new ToolApprovalRequestUpdate
            {
                ChatId = card.ChatId,
                SessionId = card.SessionId,
                ApprovalId = card.ApprovalId,
                AgentId = card.AgentId,
                AgentName = card.AgentName,
                ChatbotIconUrl = card.ChatbotIconUrl,
                ToolName = card.ToolName,
                FunctionName = card.FunctionName,
                FunctionDescription = card.FunctionDescription,
                FunctionDisplayName = card.FunctionDisplayName,
                FunctionDisplayDescription = card.FunctionDisplayDescription,
                ArgumentsJson = card.ArgumentsJson,
                ExpiresAtUtc = card.ExpiresAtUtc
            };
        }

        /// <summary>
        /// User-facing one-liner, also the card's text content (never empty: Anthropic rejects empty text blocks).
        /// WI 7726: deliberately keeps the raw function name - this text is in the chat history the model sees, and
        /// friendly display values are display-only (the UI renders them from the card data).
        /// </summary>
        public static string DescribeRequest(ToolApprovalRecord record) =>
            $"{record.AgentName} wants to use {record.FunctionName} from {record.ToolName}.";

        /// <summary>The Forms-channel approval card persisted in chat history.</summary>
        public static ChatMessageContent CreateCardMessage(ToolApprovalRecord record, DateTimeOffset now) =>
            new(AuthorRole.Assistant, DescribeRequest(record), metadata: new Dictionary<string, object>
            {
                { CommonMetadataConsts.MessageId, record.CardMessageId },
                { CommonMetadataConsts.Timestamp, now },
                { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.Forms },
                { CommonMetadataConsts.FormType, ToolApprovalConsts.FormType },
                { CommonMetadataConsts.Data, JsonConvert.SerializeObject(BuildCard(record)) }
            });

        /// <summary>Updates the persisted card of <paramref name="record"/> to its current status/decision.</summary>
        public static bool TryUpdateCard(ChatHistory chatHistory, ToolApprovalRecord record)
        {
            for (var i = 0; i < chatHistory.Count; i++)
            {
                if (GetCardApprovalId(chatHistory[i]) != record.ApprovalId)
                    continue;

                var message = chatHistory[i];
                var metadata = new Dictionary<string, object>(message.Metadata ?? new Dictionary<string, object>())
                {
                    [CommonMetadataConsts.Data] = JsonConvert.SerializeObject(BuildCard(record))
                };
                chatHistory[i] = new ChatMessageContent(message.Role, message.Items, message.ModelId, message.InnerContent, message.Encoding, metadata)
                {
                    AuthorName = message.AuthorName
                };
                return true;
            }

            return false;
        }

        /// <summary>Approval ids of every card in the history still shown as Pending.</summary>
        public static List<string> GetPendingCardApprovalIds(ChatHistory chatHistory) =>
            chatHistory
                .Select(m => (Id: GetCardApprovalId(m), Card: ReadCard(m)))
                .Where(x => x.Id != null && x.Card?.Status == ToolApprovalConsts.Status.Pending)
                .Select(x => x.Id)
                .ToList();

        /// <summary>Replaces the result of tool call <paramref name="callId"/> (its approval placeholder).</summary>
        /// <param name="onlyIfCurrent">When set, the result is only replaced while it still equals this text.</param>
        public static bool TryReplaceFunctionResult(ChatHistory chatHistory, string callId, string result, string onlyIfCurrent = null)
        {
            if (string.IsNullOrEmpty(callId))
                return false;

            foreach (var message in chatHistory)
            {
                for (var j = 0; j < message.Items.Count; j++)
                {
                    if (message.Items[j] is FunctionResultContent existing && existing.CallId == callId)
                    {
                        if (onlyIfCurrent != null && existing.Result?.ToString() != onlyIfCurrent)
                            return false;

                        message.Items[j] = new FunctionResultContent(existing.FunctionName, existing.PluginName, callId, result);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Adds a <see cref="NotRunResult"/> tool result for every tool call that has none. When a turn stops to wait for
        /// approval, calls the model issued in the same step after the waiting one never run; providers reject a
        /// history with a tool call that has no result, so every call must be answered.
        /// </summary>
        public static int CompleteDanglingFunctionCalls(ChatHistory chatHistory, string result = NotRunResult)
        {
            var answered = new HashSet<string>(chatHistory
                .SelectMany(m => m.Items.OfType<FunctionResultContent>())
                .Select(r => r.CallId)
                .Where(id => id != null), StringComparer.Ordinal);

            var added = 0;
            for (var i = 0; i < chatHistory.Count; i++)
            {
                var missing = chatHistory[i].Items.OfType<FunctionCallContent>()
                    .Where(c => c.Id != null && !answered.Contains(c.Id))
                    .ToList();
                if (missing.Count == 0)
                    continue;

                // Insert after the tool results that already follow this call message.
                var insertAt = i + 1;
                while (insertAt < chatHistory.Count && chatHistory[insertAt].Role == AuthorRole.Tool)
                    insertAt++;

                foreach (var call in missing)
                {
                    chatHistory.Insert(insertAt++, new ChatMessageContent(AuthorRole.Tool,
                        [new FunctionResultContent(call.FunctionName, call.PluginName, call.Id, result)]));
                    answered.Add(call.Id);
                    added++;
                }
            }

            return added;
        }

        /// <summary>The model-facing result text a decided (or expired) approval resolves to, when the call did not run.</summary>
        public static string ResultForNotRun(string decision) =>
            decision == ToolApprovalConsts.Decision.Deny ? DeniedResult : ExpiredResult;

        internal static string GetCardApprovalId(ChatMessageContent message)
        {
            if (!string.Equals(ReadString(message?.Metadata, CommonMetadataConsts.FormType), ToolApprovalConsts.FormType, StringComparison.Ordinal))
                return null;

            return ReadCard(message)?.ApprovalId;
        }

        internal static ToolApprovalCardData ReadCard(ChatMessageContent message)
        {
            var data = ReadString(message?.Metadata, CommonMetadataConsts.Data);
            if (string.IsNullOrWhiteSpace(data))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<ToolApprovalCardData>(data);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // Metadata survives a blob JSON round-trip as System.Text.Json.JsonElement rather than string.
        private static string ReadString(IReadOnlyDictionary<string, object> metadata, string key)
        {
            if (metadata == null || !metadata.TryGetValue(key, out var value) || value == null)
                return null;

            return value switch
            {
                string s => s,
                System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => element.GetString(),
                _ => value.ToString()
            };
        }
    }
}
