using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class ClassificationJob : Entity<int>
    {
        public int TenantId { get; set; }
        public Tenant Tenant { get; set; }

        public int AgentId { get; set; }
        public TenantAgent Agent { get; set; }

        public string JobType { get; set; }

        /// <summary>UTC timestamp of the last topic change that triggered this job. Resets on each change while Pending.</summary>
        public DateTimeOffset PreparedAt { get; set; }

        public DateTimeOffset? QueuedAt { get; set; }
        public DateTimeOffset? ProcessingStartAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }

        public bool? IsSuccess { get; set; }

        public string AIModelName { get; set; }
        public int? TotalInputTokens { get; set; }
        public int? TotalOutputTokens { get; set; }
        public int? TotalConversations { get; set; }
        public int? TotalMessages { get; set; }
        public string ErrorMessage { get; set; }
    }
}
