namespace AMAIS.WKFLO.MD.Application.Audit.Models
{
    public class AuditLogListPageResult
    {
        public List<AuditLogEntryResult> Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
}
