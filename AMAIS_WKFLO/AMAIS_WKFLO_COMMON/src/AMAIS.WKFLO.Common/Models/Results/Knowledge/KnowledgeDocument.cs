namespace AMAIS.WKFLO.Common.Models.Results.Knowledge
{
    public class KnowledgeDocument : BaseKnowledgeResult
    {
        public string Title { get; set; }
        public long Size { get; set; }
        public string Base64Content { get; set; }
    }
}
