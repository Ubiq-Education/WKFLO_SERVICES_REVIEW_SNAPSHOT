using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class MessageTopicClassification : Entity<int>
    {
        public Guid? TopicId { get; set; }
        public string TopicName { get; set; }
        public string SessionId { get; set; }
        public Guid MessageId { get; set; }
        public int AgentId { get; set; }
        public Agent Agent { get; set; }
        public DateTimeOffset MessageDate { get; set; }

        /// <summary>
        /// Message-level (per question/answer exchange) resolution judgment — independent of, and at finer
        /// granularity than, <c>Conversation.IsResolved</c> (which judges a whole conversation). Null means
        /// "not yet judged" — every row persisted before this column existed, and any message whose judgment
        /// workflow (<c>MessageResolutionWorkflow</c> or the per-message breakdown in
        /// <c>ConversationAnalysisWorkflowActivity</c>) hasn't run yet. Never falls back to
        /// <c>Conversation.IsResolved</c> — a null value here is deliberately excluded from both the
        /// "Recent resolved" and "Recent unresolved" panels rather than guessed at.
        /// </summary>
        public bool? IsResolved { get; set; }

        /// <summary>
        /// The ChatClassification tool's persisted, auto-incrementing <c>ChatClassificationToolConfig.Version</c>
        /// (as a string, via <c>ChatClassificationConfigHelper.ResolveTopicVersion</c>) at the moment this row's
        /// <c>TopicId</c>/<c>TopicName</c> were resolved — set by both classification paths (real-time and the
        /// batch <c>TopicClassificationJobHandler</c>). Null for rows persisted before this column existed, or
        /// for any classification path that didn't have a resolvable config version at write time.
        /// </summary>
        public string TopicVersion { get; set; }

        /// <summary>
        /// The MessageId of the assistant's ANSWER to this row's question — distinct from <see cref="MessageId"/>
        /// itself, which is always the USER's question (every <c>ChatMessageContent</c>, question or answer,
        /// gets its own independently-generated MessageId). Set once the resolution judgment runs
        /// (<c>MessageResolutionWorkflow</c> or <c>ConversationAnalysisWorkflowActivity</c>'s per-message
        /// breakdown), since that's the first point the answer's own MessageId is known. Exists so a rating —
        /// always given on the answer, never the question — can look this row up directly by the id it
        /// actually has (<c>RateMessageAsync</c>'s <c>messageId</c> parameter), instead of re-deriving the
        /// question from chat history. Null until judged, or for rows persisted before this column existed.
        /// </summary>
        public Guid? AnswerMessageId { get; set; }
    }
}
