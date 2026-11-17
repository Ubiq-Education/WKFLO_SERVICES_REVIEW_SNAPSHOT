using AMAIS.WKFLO.Common.Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.MessageFeedbackRated
{
    /// <summary>
    /// Raised after a message rating is successfully persisted (tenant or public/share-id chat), so it can be
    /// mirrored to DXP as the "Chatbot Feedback" analytics event.
    /// </summary>
    public class MessageFeedbackRatedDomainEvent : IWkfloNotification
    {
        public string TenantName { get; }
        public string SessionId { get; }
        public string SubjectId { get; }
        public int? TenantAgentId { get; }
        public int? PublishedTenantAgentId { get; }
        public string MessageId { get; }
        public bool IsThumbsUp { get; }
        public bool IsTestChat { get; }

        /// <summary>
        /// The topic the rated message was classified to (from <c>MessageTopicClassification.TopicId</c>,
        /// looked up by <see cref="MessageId"/>), when a classification exists. Null is a legitimate
        /// "uncorrelated" state — classification may never have run for this message — same as the
        /// "Uncategorized" handling on the Message Topic event itself, not an error.
        /// </summary>
        public Guid? TopicId { get; }
        public string TopicName { get; }

        /// <summary>The <c>MessageTopicClassification.MessageDate</c> paired with <see cref="TopicId"/>.</summary>
        public DateTimeOffset? MessageDate { get; }

        /// <summary>The underlying <c>Conversation.ConversationDate</c>, when known.</summary>
        public DateTimeOffset? ConversationDate { get; }

        /// <summary>
        /// The <c>MessageTopicClassification.TopicVersion</c> paired with <see cref="TopicId"/>, when a
        /// classification exists for the rated message. Same "legitimate uncorrelated state" null-safety as
        /// <see cref="TopicId"/>/<see cref="TopicName"/> — classification may never have run for this message.
        /// </summary>
        public string TopicVersion { get; }

        public MessageFeedbackRatedDomainEvent(
            string tenantName,
            string sessionId,
            string subjectId,
            int? tenantAgentId,
            int? publishedTenantAgentId,
            string messageId,
            bool isThumbsUp,
            bool isTestChat = false,
            Guid? topicId = null,
            string topicName = null,
            DateTimeOffset? messageDate = null,
            DateTimeOffset? conversationDate = null,
            string topicVersion = null)
        {
            TenantName = tenantName;
            SessionId = sessionId;
            SubjectId = subjectId;
            TenantAgentId = tenantAgentId;
            PublishedTenantAgentId = publishedTenantAgentId;
            MessageId = messageId;
            IsThumbsUp = isThumbsUp;
            IsTestChat = isTestChat;
            TopicId = topicId;
            TopicName = topicName;
            MessageDate = messageDate;
            ConversationDate = conversationDate;
            TopicVersion = topicVersion;
        }
    }
}
