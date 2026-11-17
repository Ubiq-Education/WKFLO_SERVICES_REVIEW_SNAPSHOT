using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Results.Knowledge.Interface;

namespace AMAIS.WKFLO.Common.Interfaces.Providers
{
    public interface IDatastreamProvider
    {
        Task BulkIndexAsync<K>(List<K> messages, string indexName) where K : class;
        Task BulkDatastreamIndexAsync<K>(List<K> messages, string datastreamName) where K : class;
        Task CreateIndexAsync(string indexName);
        Task DeleteIndexAsync(string indexName);
        Task CreateDatastreamAsync(string datastreamName);
        Task DeleteDatastreamAsync(string datastreamName);

        Task IndexAsync<K>(K message, string indexName, string pipelineName) where K : class;
        Task<IEnumerable<K>> ListAllKnowledgeAsync<K>(string tenantName, string indexName) where K : IBaseKnowledgeResult;
        Task<IEnumerable<SearchKnowledgeResult>> SearchAllKnowledgeAsync<K>(string query, List<string> tags, int limit, bool fullHightlight, string tenantName, string indexName) where K : IBaseKnowledgeResult;
        Task<bool> PutAllKnowledgePipelineAsync(string pipelineName);
        Task<bool> DeleteDocumentAsync(string docId, string indexName);
        Task<bool> ExistDocumentAsync<K>(string docId, string tenantName, string indexName) where K : IBaseKnowledgeResult;
        Task<K> GetDocumentByIdAsync<K>(string docId, string tenantName, string indexName) where K : class, IBaseKnowledgeResult;
        Task<bool> UpdateDocumentAsync<K>(string docId, K document, string tenantName, string indexName) where K : class, IBaseKnowledgeResult;
        Task<bool> UpdateDocumentTagsAsync(string docId, List<string> tags, string tenantName, string indexName);
        Task<bool> UpdateDocumentDescriptionAsync(string docId, string description, string tenantName, string indexName);
        Task<bool> RefreshIndexAsync(string indexName);
    }
}
