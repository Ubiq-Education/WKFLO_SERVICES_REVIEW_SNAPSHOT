using Microsoft.Extensions.VectorData;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers.Models
{
    /// <summary>
    /// One stored chunk of a large chat attachment (Bug 8189 / Task 8219). The key is <c>{documentId}_{chunkIndex}</c>.
    /// The embedding has a flat (exact) index: a document has at most a few thousand chunks and every query is filtered
    /// down to one chat's documents, so exact search is cheap and needs no approximate index to tune.
    /// </summary>
    public class ChatDocumentChunkEntry
    {
        [VectorStoreKey(StorageName = "id")]
        public string Id { get; init; }

        [VectorStoreData(StorageName = "tenant", IsIndexed = true)]
        public string TenantName { get; init; }

        [VectorStoreData(StorageName = "session_id", IsIndexed = true)]
        public string SessionId { get; init; }

        [VectorStoreData(StorageName = "user_id")]
        public string UserId { get; init; }

        [VectorStoreData(StorageName = "agent_id")]
        public int AgentId { get; init; }

        [VectorStoreData(StorageName = "document_id", IsIndexed = true)]
        public string DocumentId { get; init; }

        [VectorStoreData(StorageName = "file_name")]
        public string FileName { get; init; }

        [VectorStoreData(StorageName = "chunk_index")]
        public int ChunkIndex { get; init; }

        [VectorStoreData(StorageName = "page")]
        public int? Page { get; init; }

        [VectorStoreData(StorageName = "position")]
        public int Position { get; init; }

        [VectorStoreData(StorageName = "content")]
        public string Content { get; init; }

        [VectorStoreData(StorageName = "created_at", IsIndexed = true)]
        public DateTime CreatedAt { get; init; }

        [VectorStoreVector(1536, DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Flat, StorageName = "embedding")]
        public ReadOnlyMemory<float>? Embedding { get; set; }
    }
}
