using AMAIS.WKFLO.Common.Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.MessageResolutionAnalyzed
{
    /// <summary>
    /// Raised once a message's per-question/answer resolution judgment has been written to
    /// <c>MessageTopicClassification.IsResolved</c>, by either <c>MessageResolutionWorkflowActivity</c>
    /// (the near-immediate mechanism) or <c>ConversationAnalysisWorkflowActivity</c>'s per-message
    /// breakdown (the low-cost backfill mechanism), so the outcome can be mirrored to DXP as a re-report
    /// of the same "Chatbot Message Topic" event already sent at classification time — this time carrying
    /// <see cref="IsResolved"/> plus the real <see cref="Question"/>/<see cref="Response"/> text, none of
    /// which existed yet when the message was first classified. Can fire more than once for the same
    /// <see cref="MessageId"/> if a message is ever re-analyzed — DXP picks the latest by
    /// <see cref="MessageDate"/> per correlation id, same "latest wins" idiom as the conversation-level event.
    /// </summary>
    public class MessageResolutionAnalyzedDomainEvent : IWkfloNotification
    {
        public string TenantName { get; }
        public string SessionId { get; }
        public string SubjectId { get; }
        public int? TenantAgentId { get; }
        public int? PublishedTenantAgentId { get; }
        public string MessageId { get; }
        public Guid? TopicId { get; }
        public string TopicName { get; }

        /// <summary>
        /// The <c>MessageTopicClassification.TopicVersion</c> paired with <see cref="TopicId"/>/<see cref="TopicName"/>
        /// on this same row — carried through so the re-report of "Chatbot Message Topic" no longer has to send
        /// <c>topicVersion: null</c> (US 7142).
        /// </summary>
        public string TopicVersion { get; }
        public bool IsResolved { get; }
        public string Question { get; }
        public string Response { get; }
        public bool IsTestChat { get; }

        /// <summary>The <c>MessageTopicClassification.MessageDate</c> persisted at classification time.</summary>
        public DateTimeOffset MessageDate { get; }

        /// <summary>The underlying <c>Conversation.ConversationDate</c>, when known.</summary>
        public DateTimeOffset? ConversationDate { get; }

        /// <summary>
        /// The MessageId of the assistant's own answer — distinct from <see cref="MessageId"/> (the user's
        /// question). Carried through so a later rating (always given on the answer, never the question) can
        /// look this row up directly by the id it actually has.
        /// </summary>
        public string AnswerMessageId { get; }

        public MessageResolutionAnalyzedDomainEvent(
            string tenantName,
            string sessionId,
            string subjectId,
            int? tenantAgentId,
            int? publishedTenantAgentId,
            string messageId,
            Guid? topicId,
            string topicName,
            bool isResolved,
            string question,
            string response,
            bool isTestChat,
            DateTimeOffset messageDate,
            DateTimeOffset? conversationDate = null,
            string topicVersion = null,
            string answerMessageId = null)
        {
            TenantName = tenantName;
            SessionId = sessionId;
            SubjectId = subjectId;
            TenantAgentId = tenantAgentId;
            PublishedTenantAgentId = publishedTenantAgentId;
            MessageId = messageId;
            TopicId = topicId;
            TopicName = topicName;
            TopicVersion = topicVersion;
            IsResolved = isResolved;
            Question = question;
            Response = response;
            IsTestChat = isTestChat;
            MessageDate = messageDate;
            ConversationDate = conversationDate;
            AnswerMessageId = answerMessageId;
        }
    }
}
