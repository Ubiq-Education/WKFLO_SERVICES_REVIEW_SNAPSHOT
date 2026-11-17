using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class Conversation : Entity<int>
    {
        public string SessionId { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// US 7507: the first user message of the conversation (max 300 chars), shown as the second line of
        /// the MAISY conversations list. Set once, when the row is created (or the first time a user message is
        /// available), and never overwritten afterwards — unlike <see cref="Name"/>, which becomes the AI-generated
        /// title. Null means there is no preview; the UI then shows "New conversation".
        /// </summary>
        public string PreviewLine { get; set; }

        public int AgentId { get; set; }
        public TenantAgent Agent { get; set; }

        public int TenantId { get; set; }
        public Tenant Tenant { get; set; }

        public DateTimeOffset? ConversationDate { get; set; }

        public string UserId { get; set; }

        /// <summary>
        /// The DXP-side contact identifier (from <c>ChatContext.SubjectId</c>) for whoever is having this
        /// conversation. Persisted here so background paths without a live <c>ChatContext</c> — the
        /// resolution-analysis workflow activity and the batch topic-classification job — can still report
        /// DXP chatbot analytics events with the correct <c>Subject</c>.
        /// </summary>
        public string SubjectId { get; set; }

        public int TotalThumbsUp { get; set; }
        public int TotalThumbsDown { get; set; }

        public DateTimeOffset? LastActivityAt { get; set; }
        public bool IsResolved { get; set; } = true;
        public ConversationAnalysisStatus AnalysisStatus { get; set; } = ConversationAnalysisStatus.Pending;

        /// <summary>
        /// US 7507: where the AI-generated title of this conversation stands. Only MAISY conversations get one;
        /// once <see cref="ConversationTitleStatus.Generated"/>, <see cref="Name"/> holds the AI title and must not
        /// be overwritten by the per-turn sync.
        /// </summary>
        public ConversationTitleStatus TitleStatus { get; set; } = ConversationTitleStatus.Pending;

        /// <summary>
        /// True when this conversation originated from a "test chat" surface (e.g. the CMS Test Agent tab)
        /// rather than a real end user. Set from <c>ChatContext.IsTestChat</c> (propagated via the
        /// <c>amais-wkflo-is-test-chat</c> header) the first time the conversation is synced, and — once
        /// true — must never revert to false on subsequent syncs. Used to (1) hide test conversations from
        /// Chat Logs by default and (2) suppress DXP analytics reporting entirely for this conversation.
        /// </summary>
        public bool IsTestChat { get; set; }

        public ICollection<ConversationAnalysis> ConversationAnalyses { get; set; } = new List<ConversationAnalysis>();
    }
}
