namespace AMAIS.WKFLO.Common.Models.Results.Knowledge.Interface
{
    public interface IBaseKnowledgeResult
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public string TenantName { get; set; }
    }
}
