namespace AMAIS.WKFLO.MD.Application.ClassificationJobs.Models
{
    public class ClassificationJobStatusResult
    {
        public int Id { get; set; }
        public string JobType { get; set; }
        public string Status { get; set; }
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
