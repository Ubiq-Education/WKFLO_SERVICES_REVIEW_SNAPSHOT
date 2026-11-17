using AMAIS.WKFLO.Common.Models.Results.Knowledge.Interface;
using System.Text.Json.Serialization;

namespace AMAIS.WKFLO.Common.Models.Results.Knowledge
{
    public class BaseKnowledgeResult : IBaseKnowledgeResult
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }

        [JsonPropertyName("@timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        public string TenantName { get; set; }
        public List<string> Tags { get; set; }
        public string BlobUrl { get; set; }

        /// <summary>
        /// Optional, user-provided description of the document. Stored on the Elasticsearch
        /// document and surfaced back in list results (e.g. as a subtitle in the CMS UI).
        /// </summary>
        public string Description { get; set; }
    }
}
