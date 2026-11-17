namespace AMAIS.WKFLO.Common.Interfaces.Services
{
    /// <summary>
    /// Identifies the tenant/agent/session/subject a chatbot analytics event belongs to, and the DXP
    /// integration credentials to send it through. Shared by every <see cref="IDxpChatbotAnalyticsService"/>
    /// method so the four event methods don't repeat the same handful of parameters.
    /// </summary>
    /// <param name="DxpTenantName">The DXP-side tenant name (from <c>DxpIntegration.DxpTenantName</c>).</param>
    /// <param name="Domain">The DXP integration domain (from <c>DxpIntegration.Domain</c>).</param>
    /// <param name="ApiKey">The DXP integration API key (from <c>DxpIntegration.ApiKey</c>).</param>
    /// <param name="TenantId">The WKFlo tenant's numeric id. Not sent to DXP directly — kept for
    /// diagnostics/logging (see the missing-DxpIntegration-field guard in <c>DxpChatbotAnalyticsService</c>).</param>
    /// <param name="TenantName">The WKFlo tenant's name. Not sent to DXP directly — kept for
    /// diagnostics/logging (see the missing-DxpIntegration-field guard in <c>DxpChatbotAnalyticsService</c>).</param>
    /// <param name="SessionId">The chat session id — sent as DXP's <c>Object</c>, so DXP can count distinct
    /// conversations via cardinality aggregation across all chatbot analytics predicates.</param>
    /// <param name="SubjectId">The DXP contact identifier (from <c>ChatContext.SubjectId</c>) for whoever is
    /// having this conversation — sent as DXP's <c>Subject</c>. Sending is skipped entirely when this is
    /// empty/null.</param>
    /// <param name="TenantAgentId">The draft/private <c>TenantAgent.Id</c>, when known.</param>
    /// <param name="PublishedTenantAgentId">The <c>PublishedTenantAgent.Id</c>, when known.</param>
    /// <param name="ShareId">The public embed slug (<c>AgentChatSetting.ShareId</c>), when known.</param>
    /// <param name="IsTestChat">True when the underlying conversation is a "test chat" (e.g. the CMS Test
    /// Agent tab), from <c>Conversation.IsTestChat</c> or the live <c>ChatContext.IsTestChat</c> signal.
    /// When true, <c>DxpChatbotAnalyticsService.SendAsync</c> short-circuits before ever calling DXP — the
    /// single choke point that keeps test chats out of DXP analytics for all four event types.</param>
    public sealed record DxpChatbotEventContext(
        string DxpTenantName,
        string Domain,
        string ApiKey,
        int TenantId,
        string TenantName,
        string SessionId,
        string SubjectId,
        int? TenantAgentId,
        int? PublishedTenantAgentId,
        string ShareId,
        bool IsTestChat = false);

    /// <summary>
    /// Reports chatbot analytics events to DXP so it can derive conversation-level KPIs (conversation count,
    /// resolution rate, feedback totals, message topics) by aggregating on <c>Object</c> (the chat session id)
    /// across these predicates, with the person having the conversation carried as <c>Subject</c>. All four
    /// events reuse the same generic <c>POST /api/v1/interaction</c> ingestion path DXP already exposes — no
    /// new DXP pipeline needed.
    /// </summary>
    public interface IDxpChatbotAnalyticsService
    {
        /// <summary>
        /// Reports "ChatBot Conversation" (reused from the legacy in-house chatbot, for continuity) — the
        /// guaranteed baseline signal DXP uses to count conversations, independent of whether resolution
        /// analysis, feedback, or topic classification ever produce a signal for this session.
        /// </summary>
        /// <param name="conversationDate">The <c>Conversation.ConversationDate</c> this event's session was
        /// created with, sent explicitly instead of relying on ingestion time. Null only if the caller has no
        /// value to thread through.</param>
        Task<bool> ReportConversationStartedAsync(DxpChatbotEventContext context, DateTimeOffset? conversationDate = null);

        /// <summary>
        /// Reports "Chatbot Conversation Resolution". Can fire multiple times per conversation (append-only,
        /// same as the underlying <c>ConversationAnalysis</c> table) — DXP must pick the latest by
        /// <paramref name="analyzedAt"/> per correlation id.
        /// </summary>
        /// <param name="conversationDate">The underlying <c>Conversation.ConversationDate</c>, when known.</param>
        Task<bool> ReportConversationResolutionAsync(DxpChatbotEventContext context, bool isResolved, DateTimeOffset analyzedAt, DateTimeOffset? conversationDate = null);

        /// <summary>
        /// Reports "Chatbot Feedback" — once per thumbs up/down click.
        /// </summary>
        /// <param name="topicId">The topic the rated message was classified to (from
        /// <c>MessageTopicClassification.TopicId</c>), when a classification exists for that message. Null is a
        /// legitimate "uncorrelated" state — classification may never have run for this message — not an
        /// error.</param>
        /// <param name="topicName">The topic name paired with <paramref name="topicId"/>.</param>
        /// <param name="messageDate">The <c>MessageTopicClassification.MessageDate</c> paired with
        /// <paramref name="topicId"/>, when a classification exists.</param>
        /// <param name="conversationDate">The underlying <c>Conversation.ConversationDate</c>, when known.</param>
        /// <param name="topicVersion">The <c>MessageTopicClassification.TopicVersion</c> paired with
        /// <paramref name="topicId"/>, when a classification exists — same "omit rather than send empty"
        /// convention as every other optional field on this event (US 7142).</param>
        Task<bool> ReportMessageFeedbackAsync(DxpChatbotEventContext context, string messageId, bool isThumbsUp,
            Guid? topicId = null, string topicName = null, DateTimeOffset? messageDate = null, DateTimeOffset? conversationDate = null,
            string topicVersion = null);

        /// <summary>
        /// Reports "Chatbot Message Topic" — exactly ONCE per message, sent once the resolution judgment is
        /// known (<c>MessageResolutionWorkflow</c>'s near-immediate path, or <c>ConversationAnalysisWorkflowActivity</c>'s
        /// per-message breakdown), carrying topic identity and the resolution outcome together. Classification
        /// itself (real-time or batch re-classification) only persists <c>MessageTopicClassification</c> to SQL —
        /// it deliberately does NOT call this method, since the answer doesn't exist yet at that point and a
        /// message must never produce two separate DXP documents. The one exception is the batch
        /// <c>TopicClassificationJobHandler</c>'s periodic re-classification, which re-reports topic identity
        /// alone (no resolution fields) to self-heal `TopicVersion` drift over historical data — a legitimate,
        /// time-separated re-report, not a same-turn duplicate. <paramref name="topicId"/>/<paramref name="topicName"/>
        /// are null for a "NONE"/unparseable result — that's still a real row, not skipped.
        /// <paramref name="topicVersion"/> is the ChatClassification tool's persisted, auto-incrementing
        /// <c>ChatClassificationToolConfig.Version</c> (as a string) at send time, so DXP can avoid mixing
        /// stats across topic-set changes.
        /// </summary>
        /// <param name="messageDate">The <c>MessageTopicClassification.MessageDate</c> computed/persisted at
        /// classification time.</param>
        /// <param name="conversationDate">The underlying <c>Conversation.ConversationDate</c>, when known.</param>
        /// <param name="isResolved">The message-level (per question/answer exchange) resolution judgment from
        /// <c>MessageTopicClassification.IsResolved</c>.</param>
        /// <param name="question">The visitor's actual question text for this message. Forward-looking: enables
        /// a future full-text search feature over real conversation content in DXP; not consumed by any UI
        /// today.</param>
        /// <param name="response">The bot's actual answer text for this message.</param>
        /// <param name="answerMessageId">The MessageId of the assistant's own answer — distinct from
        /// <paramref name="messageId"/> (the user's question this whole event is keyed by). Present only
        /// alongside <paramref name="isResolved"/>/<paramref name="question"/>/<paramref name="response"/>,
        /// since the answer (and its own MessageId) doesn't exist yet at classification time. Forward-looking,
        /// same as <paramref name="question"/>/<paramref name="response"/> — lets a future feature correlate
        /// by either the question's or the answer's id.</param>
        Task<bool> ReportMessageTopicAsync(DxpChatbotEventContext context, string messageId, Guid? topicId, string topicName, string topicVersion,
            DateTimeOffset? messageDate = null, DateTimeOffset? conversationDate = null,
            bool? isResolved = null, string question = null, string response = null, string answerMessageId = null);
    }
}
