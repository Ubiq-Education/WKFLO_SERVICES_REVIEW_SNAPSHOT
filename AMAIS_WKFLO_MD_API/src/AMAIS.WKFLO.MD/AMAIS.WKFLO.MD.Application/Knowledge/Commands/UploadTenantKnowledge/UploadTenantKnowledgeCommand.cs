using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.UploadTenantKnowledge
{
    public class UploadTenantKnowledgeCommand : IWkfloRequest<Result>
    {
        public string FileName { get; set; }
        public string Title { get; set; }

        /// <summary>
        /// Optional, user-provided description of the document (e.g. "Full admissions process
        /// and requirements"). Persisted on the Elasticsearch document and returned from the
        /// list endpoint.
        /// </summary>
        public string Description { get; set; }
        public string ContentType { get; set; }
        [JsonIgnore]
        public string Base64Content { get; set; }
        public long Size { get; set; }
        public List<string> Tags { get; set; }
        public string BlobUrl { get; set; }
    }
}

