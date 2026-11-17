using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Results.Knowledge.Interface;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    public class ElasticsearchDatastreamProvider : IDatastreamProvider
    {
        private readonly ElasticsearchClient _elasticsearchClient;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<ElasticsearchDatastreamProvider> _logger;

        public ElasticsearchDatastreamProvider(ElasticsearchClient elasticsearchClient, IDateTimeProvider dateTimeProvider, ILogger<ElasticsearchDatastreamProvider> logger)
        {
            _elasticsearchClient = elasticsearchClient;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        public async Task BulkDatastreamIndexAsync<K>(List<K> messages, string datastreamName) where K : class
        {
            // NEED testing
            var dataStreamResponse = await _elasticsearchClient.Indices.GetDataStreamAsync(new GetDataStreamRequest(datastreamName));
            dataStreamResponse.DataStreams.ToList().ForEach(async m =>
            {
                await BulkIndexAsync(messages, m.Indices.Last().IndexName);
            });
        }

        public async Task BulkIndexAsync<K>(List<K> messages, string indexName) where K : class
        {
            var bulkResponse = await _elasticsearchClient.IndexManyAsync(messages, indexName);

            if (bulkResponse != null && bulkResponse.ItemsWithErrors != null)
            {
                foreach (var indexResponse in bulkResponse.ItemsWithErrors)
                {
                    if (indexResponse != null && indexResponse.Shards != null && indexResponse.Shards.Failures != null && indexResponse.Shards.Failures.Any())
                    {
                        foreach (var failure in indexResponse.Shards.Failures)
                        {
                            _logger.LogWarning("Failure while writing the message {Status} {Index} {Shard} {Node} {Reason}",
                                failure.Status,
                                failure.Index,
                                failure.Shard,
                                failure.Node,
                                failure.Reason
                            );
                        }
                    }
                    if (indexResponse != null && indexResponse.Error != null)
                    {
                        _logger.LogWarning("Failure while writing the message {Reason} {Index}",
                                indexResponse.Error.Reason,
                                indexResponse.Index
                            );
                    }
                }
            }
        }

        public async Task CreateDatastreamAsync(string datastreamName)
        {
            var creationResponse = await _elasticsearchClient.Indices.CreateDataStreamAsync(datastreamName);
            if (!creationResponse.IsSuccess() || !creationResponse.IsValidResponse)
            {
                _logger.LogWarning("Failure while creating Datastream {ElasticsearchWarnings} {ElasticsearchServerError}",
                                creationResponse.ElasticsearchWarnings?.ToString(),
                                creationResponse.ElasticsearchServerError?.ToString()
                            );
            }
        }

        public async Task CreateIndexAsync(string indexName)
        {
            var creationResponse = await _elasticsearchClient.Indices.CreateAsync(indexName);
            if (!creationResponse.IsSuccess() || !creationResponse.IsValidResponse)
            {
                _logger.LogWarning("Failure while creating Index {ElasticsearchWarnings} {ElasticsearchServerError}",
                                creationResponse.ElasticsearchWarnings?.ToString(),
                                creationResponse.ElasticsearchServerError?.ToString()
                            );
            }
        }

        public async Task DeleteDatastreamAsync(string datastreamName)
        {
            var deleteResponse = await _elasticsearchClient.Indices.DeleteDataStreamAsync(datastreamName);
            if (!deleteResponse.IsSuccess() || !deleteResponse.IsValidResponse)
            {
                _logger.LogWarning("Failure while deleting Datastream {ElasticsearchWarnings} {ElasticsearchServerError}",
                                deleteResponse.ElasticsearchWarnings?.ToString(),
                                deleteResponse.ElasticsearchServerError?.ToString()
                            );
            }
        }

        public async Task DeleteIndexAsync(string indexName)
        {
            var deleteResponse = await _elasticsearchClient.Indices.DeleteAsync(indexName);
            if (!deleteResponse.IsSuccess() || !deleteResponse.IsValidResponse)
            {
                _logger.LogWarning("Failure while deleting Index {ElasticsearchWarnings} {ElasticsearchServerError}",
                                deleteResponse.ElasticsearchWarnings?.ToString(),
                                deleteResponse.ElasticsearchServerError?.ToString()
                            );
            }
        }

        public async Task IndexAsync<K>(K message, string indexName, string pipelineName) where K : class
        {
            var docId = $"file_{_dateTimeProvider.DateTimeOffsetUtcNow().Ticks.ToString()}_{new Random().Next().ToString()}";
            var indexResponse = await _elasticsearchClient.IndexAsync(message, indexName, docId, prop => prop.Pipeline(pipelineName));
            if (indexResponse != null && indexResponse.Shards != null && indexResponse.Shards.Failures != null && indexResponse.Shards.Failures.Any())
            {
                foreach (var failure in indexResponse.Shards.Failures)
                {
                    _logger.LogWarning("Failure while writing the interaction message {Status} {Index} {Shard} {Node} {Reason}",
                        failure.Status,
                        failure.Index,
                        failure.Shard,
                        failure.Node,
                        failure.Reason
                    );
                }
            }
        }

        public async Task<IEnumerable<K>> ListAllKnowledgeAsync<K>(string tenantName, string indexName) where K : IBaseKnowledgeResult
        {
            var searchResult = await _elasticsearchClient.SearchAsync<K>(s =>
                s.Indices(indexName).Size(1000)
                .Query(
                    q => q.Bool(b => b
                        .Must(m => m.MatchAll())
                        .Filter(f => f.Term(t => t.Field("tenantName.keyword").Value(tenantName).CaseInsensitive(true)))
                    )
                )
            );

            var documents = searchResult.Hits != null ?
                searchResult.Hits.Select(hit =>
                {
                    var doc = hit.Source;
                    doc.Id = hit.Id; // Populate the Id from the Elasticsearch document ID
                    return doc;
                }).OrderByDescending(m => m.Timestamp).ToList()
                : new List<K>();

            if (searchResult.IsValidResponse && searchResult.Total > 0)
            {
                return documents;
            }
            else
            {
                _logger.LogWarning("Failure while searching the index {ElasticsearchWarnings} {ElasticsearchServerError}",
                                searchResult.ElasticsearchWarnings?.ToString(),
                                searchResult.ElasticsearchServerError?.ToString()
                            );
                return new List<K>();
            }
        }

        public async Task<IEnumerable<SearchKnowledgeResult>> SearchAllKnowledgeAsync<K>(string query, List<string> tags, int limit, bool fullHightlight, string tenantName, string indexName) where K : IBaseKnowledgeResult
        {
            // Create filter list
            var filters = new List<Action<QueryDescriptor<K>>>
            {
                // Tenant filter
                f => f.Term(t => t.Field("tenantName.keyword").Value(tenantName).CaseInsensitive(true))
            };

            // Add tags filter if needed
            if (tags != null && tags.Any())
            {
                filters.Add(f => f.Terms(t => t.Field("tags.keyword").Terms(terms =>
                    terms.Value(tags.Select(tag => FieldValue.FromValue(tag)).ToList()))));
            }

            var searchResult = await _elasticsearchClient.SearchAsync<K>(s =>
                s.Indices(indexName)
                .Size(limit)
                .TrackScores(true)
                .Query(
                    q => q.Bool(b => b
                        .Must(
                            m => m.Semantic(mt => mt
                                .Field("semanticData")
                                .Query(query)
                            )
                        )
                        .Filter(filters.ToArray())
                    )
                )
                .Highlight(h => h
                    .PreTags("<strong>")
                    .PostTags("</strong>")
                    .Fields(
                        f => f.Add(new Field("semanticData"))
                    )
                )
            );

            var maxScore = searchResult?.HitsMetadata?.MaxScore ?? 0;

            var documents = searchResult.Hits != null ?
                searchResult.Hits.Select(hit =>
                {
                    var doc = hit.Source;

                    var score = hit.Score ?? 0;

                    var normalizedScore = maxScore > 0 ? (score / maxScore) : 0;

                    var text = (hit.Highlight?.ContainsKey("semanticData")).GetValueOrDefault()
                            ? string.Join(" ... ", hit.Highlight["semanticData"])
                            : string.Empty;

                    if (!fullHightlight)
                    {
                        if (text.Length > 300)
                        {
                            text = text.Substring(0, 297) + "...";
                        }
                    }

                    return new SearchKnowledgeResult
                    {
                        Id = hit.Id,
                        FileName = doc.FileName,
                        Timestamp = doc.Timestamp,
                        TenantName = doc.TenantName,
                        Similarity = normalizedScore,
                        Text = text
                    };
                }).Where(m => m != null).OrderByDescending(m => m.Similarity)
                    .ThenByDescending(m => m.Timestamp)
                    .ToList()
                : new List<SearchKnowledgeResult>();

            if (searchResult.IsValidResponse && searchResult.Total > 0)
            {
                return documents;
            }
            else
            {
                _logger.LogWarning("Failure while searching the index {ElasticsearchWarnings} {ElasticsearchServerError}",
                                searchResult.ElasticsearchWarnings?.ToString(),
                                searchResult.ElasticsearchServerError?.ToString()
                            );
                return new List<SearchKnowledgeResult>();
            }
        }

        public async Task<bool> PutAllKnowledgePipelineAsync(string pipelineName)
        {
            var pipeResult = await _elasticsearchClient.Ingest.PutPipelineAsync(pipelineName, prop => prop
                .Processors(p => p
                    .Attachment(a => a.Field("base64Content")
                        .IndexedChars(-1)
                        .RemoveBinary(true)
                    )
                )
            );

            if (pipeResult.IsSuccess())
            {
                return true;
            }
            else
            {
                _logger.LogWarning("Failure while creating pipelines {ElasticsearchWarnings} {ElasticsearchServerError}",
                                pipeResult.ElasticsearchWarnings?.ToString(),
                                pipeResult.ElasticsearchServerError?.ToString()
                            );
                return false;
            }
        }

        public async Task<bool> DeleteDocumentAsync(string docId, string indexName)
        {
            var deleteResponse = await _elasticsearchClient.DeleteAsync(indexName, docId);

            if (!deleteResponse.IsValidResponse)
            {
                _logger.LogWarning("Failure while deleting document {DocId} from index {Index}. {ElasticsearchWarnings} {ElasticsearchServerError}",
                    docId,
                    indexName,
                    deleteResponse.ElasticsearchWarnings?.ToString(),
                    deleteResponse.ElasticsearchServerError?.ToString()
                );
                return false;
            }
            else if (deleteResponse.Result != Result.Deleted)
            {
                _logger.LogWarning("Document {DocId} not found in index {Index} or couldn't be deleted. Result: {Result}",
                    docId,
                    indexName,
                    deleteResponse.Result
                );
                return false;
            }

            return true;
        }

        public async Task<bool> ExistDocumentAsync<K>(string docId, string tenantName, string indexName) where K : IBaseKnowledgeResult
        {
            // A null term value makes Elasticsearch reject the whole query with a parse error
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                _logger.LogWarning("Skipping document existence check for {DocId} in index {Index}: tenant name is empty", docId, indexName);
                return false;
            }

            try
            {
                var searchResponse = await _elasticsearchClient.SearchAsync<K>(s => s
                    .Indices(indexName)
                    .Query(q => q
                        .Bool(b => b
                            .Must(
                                m => m.Ids(ids => ids.Values(docId)),
                                m => m.Term(t => t.Field("tenantName.keyword").Value(tenantName).CaseInsensitive(true))
                            )
                        )
                    )
                );

                if (!searchResponse.IsValidResponse)
                {
                    _logger.LogWarning("Failure while checking document existence {DocId} in index {Index}. {ElasticsearchWarnings} {ElasticsearchServerError}",
                        docId,
                        indexName,
                        searchResponse.ElasticsearchWarnings?.ToString(),
                        searchResponse.ElasticsearchServerError?.ToString()
                    );
                    return false;
                }

                return searchResponse.Hits.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking document existence for {DocId} in index {Index}", docId, indexName);
                return false;
            }
        }

        public async Task<K> GetDocumentByIdAsync<K>(string docId, string tenantName, string indexName) where K : class, IBaseKnowledgeResult
        {
            // A null term value makes Elasticsearch reject the whole query with a parse error
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                _logger.LogWarning("Skipping get of document {DocId} from index {Index}: tenant name is empty", docId, indexName);
                return null;
            }

            try
            {
                var searchResponse = await _elasticsearchClient.SearchAsync<K>(s => s
                    .Indices(indexName)
                    .Query(q => q
                        .Bool(b => b
                            .Must(
                                m => m.Ids(ids => ids.Values(docId)),
                                m => m.Term(t => t.Field("tenantName.keyword").Value(tenantName).CaseInsensitive(true))
                            )
                        )
                    )
                );

                if (!searchResponse.IsValidResponse)
                {
                    _logger.LogWarning("Failure while getting document {DocId} from index {Index}. {ElasticsearchWarnings} {ElasticsearchServerError}",
                        docId,
                        indexName,
                        searchResponse.ElasticsearchWarnings?.ToString(),
                        searchResponse.ElasticsearchServerError?.ToString()
                    );
                    return null;
                }

                if (searchResponse.Hits.Count == 0)
                {
                    return null;
                }

                var hit = searchResponse.Hits.First();
                var document = hit.Source;
                document.Id = hit.Id;

                return document;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting document {DocId} from index {Index}", docId, indexName);
                return null;
            }
        }

        public async Task<bool> UpdateDocumentAsync<K>(string docId, K document, string tenantName, string indexName) where K : class, IBaseKnowledgeResult
        {
            try
            {
                // First verify that the document exists and belongs to the correct tenant
                var exists = await ExistDocumentAsync<K>(docId, tenantName, indexName);
                if (!exists)
                {
                    _logger.LogWarning("Document {DocId} not found in index {Index} or doesn't belong to tenant {TenantName}",
                        docId,
                        indexName,
                        tenantName);
                    return false;
                }

                // Ensure the document has the correct ID and tenant
                document.Id = docId;
                document.TenantName = tenantName;

                // Update the document in Elasticsearch
                var updateResponse = await _elasticsearchClient.IndexAsync(
                    document,
                    indexName,
                    docId);

                if (!updateResponse.IsValidResponse)
                {
                    _logger.LogWarning("Failure while updating document {DocId} in index {Index}. {ElasticsearchWarnings} {ElasticsearchServerError}",
                        docId,
                        indexName,
                        updateResponse.ElasticsearchWarnings?.ToString(),
                        updateResponse.ElasticsearchServerError?.ToString());
                    return false;
                }

                // Verify that the document was updated successfully
                if (updateResponse.Result != Result.Updated && updateResponse.Result != Result.Created)
                {
                    _logger.LogWarning("Document {DocId} update failed in index {Index}. Result: {Result}",
                        docId,
                        indexName,
                        updateResponse.Result);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating document {DocId} in index {Index}", docId, indexName);
                return false;
            }
        }
        public async Task<bool> UpdateDocumentTagsAsync(string docId, List<string> tags, string tenantName, string indexName)
        {
            try
            {
                // Verify that the document exists and belongs to the correct tenant
                var exists = await ExistDocumentAsync<BaseKnowledgeResult>(docId, tenantName, indexName);
                if (!exists)
                {
                    _logger.LogWarning("Document {DocId} not found in index {Index} or doesn't belong to tenant {TenantName}",
                        docId,
                        indexName,
                        tenantName);
                    return false;
                }

                // Update only the tags field using a partial update
                var updateResponse = await _elasticsearchClient.UpdateAsync<BaseKnowledgeResult, object>(
                    indexName,
                    docId,
                    u => u.Doc(new { tags = tags })
                );

                if (!updateResponse.IsValidResponse)
                {
                    _logger.LogWarning("Failure while updating tags for document {DocId} in index {Index}. {ElasticsearchWarnings} {ElasticsearchServerError}",
                        docId,
                        indexName,
                        updateResponse.ElasticsearchWarnings?.ToString(),
                        updateResponse.ElasticsearchServerError?.ToString());
                    return false;
                }

                // Verify that the document was updated correctly
                if (updateResponse.Result != Result.Updated && updateResponse.Result != Result.NoOp)
                {
                    _logger.LogWarning("Document {DocId} tags update failed in index {Index}. Result: {Result}",
                        docId,
                        indexName,
                        updateResponse.Result);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating tags for document {DocId} in index {Index}", docId, indexName);
                return false;
            }
        }

        public async Task<bool> UpdateDocumentDescriptionAsync(string docId, string description, string tenantName, string indexName)
        {
            try
            {
                // Verify that the document exists and belongs to the correct tenant
                var exists = await ExistDocumentAsync<BaseKnowledgeResult>(docId, tenantName, indexName);
                if (!exists)
                {
                    _logger.LogWarning("Document {DocId} not found in index {Index} or doesn't belong to tenant {TenantName}",
                        docId,
                        indexName,
                        tenantName);
                    return false;
                }

                // Update only the description field using a partial update
                var updateResponse = await _elasticsearchClient.UpdateAsync<BaseKnowledgeResult, object>(
                    indexName,
                    docId,
                    u => u.Doc(new { description = description })
                );

                if (!updateResponse.IsValidResponse)
                {
                    _logger.LogWarning("Failure while updating description for document {DocId} in index {Index}. {ElasticsearchWarnings} {ElasticsearchServerError}",
                        docId,
                        indexName,
                        updateResponse.ElasticsearchWarnings?.ToString(),
                        updateResponse.ElasticsearchServerError?.ToString());
                    return false;
                }

                // Verify that the document was updated correctly
                if (updateResponse.Result != Result.Updated && updateResponse.Result != Result.NoOp)
                {
                    _logger.LogWarning("Document {DocId} description update failed in index {Index}. Result: {Result}",
                        docId,
                        indexName,
                        updateResponse.Result);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating description for document {DocId} in index {Index}", docId, indexName);
                return false;
            }
        }

        public async Task<bool> RefreshIndexAsync(string indexName)
        {
            try
            {
                var refreshResponse = await _elasticsearchClient.Indices.RefreshAsync(indexName);

                if (!refreshResponse.IsValidResponse)
                {
                    _logger.LogWarning("Failure while refreshing index {Index}. {ElasticsearchWarnings} {ElasticsearchServerError}",
                        indexName,
                        refreshResponse.ElasticsearchWarnings?.ToString(),
                        refreshResponse.ElasticsearchServerError?.ToString());
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing index {Index}", indexName);
                return false;
            }
        }
    }
}
