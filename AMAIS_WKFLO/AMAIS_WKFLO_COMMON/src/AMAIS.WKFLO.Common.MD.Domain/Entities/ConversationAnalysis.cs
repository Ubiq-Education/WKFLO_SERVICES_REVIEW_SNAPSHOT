using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public abstract class ConversationAnalysis : Entity<int>
    {
        public int ConversationId { get; set; }
        public Conversation Conversation { get; set; }
        public DateTimeOffset AnalyzedAt { get; set; }
    }
}
