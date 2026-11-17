using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Helpers
{
    /// <summary>
    /// Shared, provider-agnostic helpers for pulling facts back out of a persisted <c>ChatHistory</c> by a
    /// user message's own <c>MessageId</c> metadata. Used by both the AI API's resolution-judgment activities
    /// (<c>ConversationAnalysisWorkflowActivity</c>, per-message backfill) and the Worker's topic
    /// reclassification job (<c>TopicClassificationJobHandler</c>) — moved here (rather than left in the AI
    /// API's own project) specifically so the Worker, which cannot reference AI API's project, can reuse the
    /// exact same extraction logic instead of a parallel, drift-prone copy.
    /// </summary>
    public static class ChatHistoryMessageExtractor
    {
        /// <summary>
        /// Bug 8189 / Task 8219: whether a message only carries an attachment, not something the user typed: an image (file name on the
        /// message metadata) or a document (file name on its text item), whether it holds the document's short message or, in the old
        /// format, its whole text. Such a message is not a question and not conversation text for analysis or topic classification.
        /// </summary>
        public static bool IsAttachment(ChatMessageContent message) =>
            message.Role == AuthorRole.User
            && (message.Metadata?.ContainsKey(CommonMetadataConsts.FileName) == true
                || message.Items.Any(i => i.Metadata?.ContainsKey(CommonMetadataConsts.FileName) == true));

        /// <summary>
        /// Finds the question text for <paramref name="messageId"/> and, skipping past any ContactForm filler
        /// reply (<see cref="CommonMetadataConsts.IsContactFormFiller"/>), any "Stopped" divider
        /// (<see cref="CommonMetadataConsts.IsStoppedMarker"/>, US 7502) AND any blank-content Assistant
        /// message, the first REAL Assistant answer after it. Returns a null answer (never the filler, never a
        /// blank message) if no real answer exists yet — callers must treat a null answer as "not ready to
        /// judge/report", never as an empty-string answer.
        /// </summary>
        /// <remarks>
        /// Live-testing find (US 7142): a tool-invoking turn's OWN Assistant message (the one SK adds to carry
        /// the <c>FunctionCallContent</c>, e.g. for the ContactForm tool) has an empty/blank <c>Content</c> and
        /// is NOT itself tagged <see cref="CommonMetadataConsts.IsContactFormFiller"/> (only the LATER,
        /// actual text reply produced once the tool result comes back gets that tag) — so without the blank-
        /// content check, this would match that intermediate step as "the answer" instead of skipping past it,
        /// silently reporting an empty (thus omitted, per <c>DxpChatbotAnalyticsService</c>'s AddIfPresent
        /// convention) Response for a message that DOES have a real, later, non-blank answer.
        /// </remarks>
        public static (string question, string answer, Guid? answerMessageId) ExtractQuestionAndAnswer(ChatHistory chatHistory, Guid messageId)
        {
            var messages = chatHistory.ToList();
            var index = messages.FindIndex(m => GetMessageIdFromMetadata(m.Metadata) == messageId);
            if (index < 0)
                return (null, null, null);

            // Bounded at the next User message so a turn that never produced a real answer (e.g. the
            // near-immediate MessageResolutionWorkflow's own guard against this) can never "steal" a LATER,
            // unrelated question's answer as its own. Safe for the ContactForm replay shape this otherwise
            // has to cross (blank function-call message -> tool result -> filler -> real answer), since the
            // replay's own user message is ephemeral and stripped before persistence - there is no User
            // message between the original question and its eventual real answer.
            var answerMessage = messages
                .Skip(index + 1)
                .TakeWhile(m => m.Role != AuthorRole.User)
                .FirstOrDefault(m => m.Role == AuthorRole.Assistant
                    && !string.IsNullOrWhiteSpace(m.Content)
                    && !IsContactFormFillerMessage(m.Metadata)
                    && !IsFlagSet(m.Metadata, CommonMetadataConsts.IsToolApprovalNotice)
                    && !IsFlagSet(m.Metadata, CommonMetadataConsts.IsStoppedMarker));

            if (answerMessage == null)
                return (messages[index].Content, null, null);

            return (messages[index].Content, answerMessage.Content, GetMessageIdFromMetadata(answerMessage.Metadata));
        }

        /// <summary>
        /// Reverse of <see cref="ExtractQuestionAndAnswer"/>: given the ANSWER's own MessageId, finds the
        /// nearest preceding User message and returns ITS MessageId (the question's own row key in
        /// <c>MessageTopicClassification</c>). Used as a fallback when a rating handler's primary lookup by
        /// <c>AnswerMessageId</c> misses because <c>MessageResolutionWorkflow</c> - which persists that
        /// column - hasn't run yet (it's scheduled asynchronously, after the response has already been
        /// returned to the visitor, so a fast rating can genuinely race it). The question's own MessageId is
        /// set synchronously at classification time, before the answer even exists, so looking up by it
        /// instead is never subject to that race - if a classification row exists for this question at all,
        /// this lookup finds it regardless of whether resolution has been judged yet.
        /// </summary>
        public static Guid? FindQuestionMessageIdForAnswer(ChatHistory chatHistory, Guid answerMessageId)
        {
            if (chatHistory == null)
                return null;

            var messages = chatHistory.ToList();
            var index = messages.FindIndex(m => GetMessageIdFromMetadata(m.Metadata) == answerMessageId);
            if (index < 0)
                return null;

            for (var i = index - 1; i >= 0; i--)
            {
                if (messages[i].Role == AuthorRole.User)
                    return GetMessageIdFromMetadata(messages[i].Metadata);
            }

            return null;
        }

        public static Guid? GetMessageIdFromMetadata(IReadOnlyDictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.MessageId, out var messageIdObj) || messageIdObj == null)
                return null;

            var raw = messageIdObj switch
            {
                JsonElement element => element.GetString(),
                string s => s,
                _ => messageIdObj.ToString()
            };

            return Guid.TryParse(raw, out var parsed) ? parsed : null;
        }

        /// <summary>
        /// Reads the real send timestamp a message was tagged with at persistence time
        /// (<see cref="CommonMetadataConsts.Timestamp"/>) — the message's own, actual date, never "now".
        /// Returns null if absent/unparseable so callers can fall back explicitly rather than silently get a
        /// wrong date.
        /// </summary>
        public static DateTimeOffset? GetTimestampFromMetadata(IReadOnlyDictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.Timestamp, out var value) || value == null)
                return null;

            switch (value)
            {
                case DateTimeOffset dto:
                    return dto;
                case JsonElement element when element.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(element.GetString(), out var parsedFromElement):
                    return parsedFromElement;
                case string s when DateTimeOffset.TryParse(s, out var parsedFromString):
                    return parsedFromString;
                default:
                    return null;
            }
        }

        private static bool IsContactFormFillerMessage(IReadOnlyDictionary<string, object> metadata) =>
            IsFlagSet(metadata, CommonMetadataConsts.IsContactFormFiller);

        internal static bool IsFlagSet(IReadOnlyDictionary<string, object> metadata, string key)
        {
            if (metadata == null || !metadata.TryGetValue(key, out var value) || value == null)
                return false;

            return value switch
            {
                JsonElement element => element.ValueKind == JsonValueKind.True,
                bool b => b,
                _ => bool.TryParse(value.ToString(), out var parsed) && parsed
            };
        }
    }
}
