using AMAIS.WKFLO.Common.Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.ConversationResolutionAnalyzed
{
    /// <summary>
    /// Raised right after <c>ConversationAnalysisWorkflowActivity.CompleteAnalysisAsync</c> inserts a
    /// <c>ResolvedConversationAnalysisResult</c> row, so the outcome can be mirrored to DXP as the
    /// "Chatbot Conversation Resolution" analytics event. Can fire multiple times for the same
    /// <see cref="SessionId"/> — re-analysis is append-only, same as the underlying table.
    /// </summary>
    public class ConversationResolutionAnalyzedDomainEvent : IWkfloNotification
    {
        public string TenantName { get; }
        public string SessionId { get; }
        public string SubjectId { get; }
        public int? TenantAgentId { get; }
        public int? PublishedTenantAgentId { get; }
        public bool IsResolved { get; }
        public DateTimeOffset AnalyzedAt { get; }
        public bool IsTestChat { get; }

        /// <summary>The underlying <c>Conversation.ConversationDate</c>, when known.</summary>
        public DateTimeOffset? ConversationDate { get; }

        public ConversationResolutionAnalyzedDomainEvent(
            string tenantName,
            string sessionId,
            string subjectId,
            int? tenantAgentId,
            int? publishedTenantAgentId,
            bool isResolved,
            DateTimeOffset analyzedAt,
            bool isTestChat = false,
            DateTimeOffset? conversationDate = null)
        {
            TenantName = tenantName;
            SessionId = sessionId;
            SubjectId = subjectId;
            TenantAgentId = tenantAgentId;
            PublishedTenantAgentId = publishedTenantAgentId;
            IsResolved = isResolved;
            AnalyzedAt = analyzedAt;
            IsTestChat = isTestChat;
            ConversationDate = conversationDate;
        }
    }
}
