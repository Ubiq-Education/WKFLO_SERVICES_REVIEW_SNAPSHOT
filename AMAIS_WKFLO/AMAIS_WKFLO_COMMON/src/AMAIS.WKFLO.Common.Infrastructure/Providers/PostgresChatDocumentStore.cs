using AMAIS.WKFLO.Common.Infrastructure.Providers.Models;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Documents;
using AMAIS.WKFLO.Common.Models.Settings;
using CommunityToolkit.VectorData.PgVector;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Npgsql;
using System.Linq.Expressions;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    /// <summary>
    /// <see cref="IChatDocumentStore"/> on the shared Postgres vector collection (<see cref="ChatDocumentSettings.CollectionName"/>),
    /// created on first use like the long-term memory collections. See <see cref="ChatDocumentFilters"/> for the filters.
    /// </summary>
    public class PostgresChatDocumentStore : IChatDocumentStore
    {
        private const int PageSize = 500;

        private readonly VectorStoreCollection<string, ChatDocumentChunkEntry> _collection;
        private readonly SemaphoreSlim _ensureLock = new(1, 1);
        private bool _ensured;

        public PostgresChatDocumentStore(INpgsqlProvider npgsqlProvider, IOptions<ChatDocumentSettings> settings)
        {
            var store = new PostgresVectorStore((NpgsqlDataSource)npgsqlProvider.GetDataSource(), true);
            _collection = store.GetCollection<string, ChatDocumentChunkEntry>(settings.Value.CollectionName);
        }

        public async Task UpsertAsync(ChatDocumentScope scope, IReadOnlyList<ChatDocumentChunk> chunks, CancellationToken cancellationToken = default)
        {
            if (chunks.Count == 0)
                return;

            await EnsureCollectionAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var entries = chunks.Select(c => new ChatDocumentChunkEntry
            {
                Id = Key(c.DocumentId, c.ChunkIndex),
                TenantName = scope.TenantName,
                SessionId = scope.SessionId,
                UserId = scope.UserId,
                AgentId = c.AgentId,
                DocumentId = c.DocumentId.ToString("D"),
                FileName = c.FileName,
                ChunkIndex = c.ChunkIndex,
                Page = c.Page,
                Position = c.Position,
                Content = c.Content,
                CreatedAt = now,
                Embedding = c.Embedding
            }).ToList();

            await _collection.UpsertAsync(entries, cancellationToken);
        }

        public async Task<IReadOnlyList<ChatDocumentPassage>> SearchAsync(ChatDocumentScope scope, IReadOnlyCollection<Guid> documentIds, ReadOnlyMemory<float> queryEmbedding,
            int top, double minScore, string fileName = null, CancellationToken cancellationToken = default)
        {
            if (documentIds.Count == 0)
                return [];

            await EnsureCollectionAsync(cancellationToken);

            var options = new VectorSearchOptions<ChatDocumentChunkEntry> { Filter = ChatDocumentFilters.ForScope(scope, documentIds, fileName) };
            var passages = new List<ChatDocumentPassage>();

            await foreach (var result in _collection.SearchAsync(queryEmbedding, top, options, cancellationToken))
            {
                // The collection uses cosine distance (lower is closer): 1 - distance is the similarity.
                var similarity = 1 - (result.Score ?? 1);
                if (similarity >= minScore)
                    passages.Add(ToPassage(result.Record, similarity));
            }

            return passages.OrderByDescending(p => p.Score).ToList();
        }

        public async Task<IReadOnlyList<ChatDocumentPassage>> GetChunksAsync(ChatDocumentScope scope, Guid documentId, IReadOnlyCollection<int> chunkIndexes,
            CancellationToken cancellationToken = default)
        {
            if (chunkIndexes.Count == 0)
                return [];

            await EnsureCollectionAsync(cancellationToken);

            var passages = new List<ChatDocumentPassage>();
            await foreach (var entry in _collection.GetAsync(ChatDocumentFilters.ForChunks(scope, documentId, chunkIndexes), chunkIndexes.Count, cancellationToken: cancellationToken))
                passages.Add(ToPassage(entry, 0));

            return passages.OrderBy(p => p.ChunkIndex).ToList();
        }

        public async Task<int> CopyDocumentAsync(ChatDocumentScope scope, Guid sourceDocumentId, Guid targetDocumentId, int targetAgentId, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var filter = ChatDocumentFilters.ForScope(scope, [sourceDocumentId]);
            var copied = 0;

            // Pages of chunks (with their vectors) are re-keyed to the target document: nothing leaves the scope.
            while (true)
            {
                var options = new FilteredRecordRetrievalOptions<ChatDocumentChunkEntry>
                {
                    Skip = copied,
                    IncludeVectors = true,
                    OrderBy = o => o.Ascending(e => e.ChunkIndex)
                };

                var page = new List<ChatDocumentChunkEntry>();
                await foreach (var entry in _collection.GetAsync(filter, PageSize / 5, options, cancellationToken))
                    page.Add(entry);

                if (page.Count == 0)
                    break;

                var copies = page.Select(e => new ChatDocumentChunkEntry
                {
                    Id = Key(targetDocumentId, e.ChunkIndex),
                    TenantName = e.TenantName,
                    SessionId = e.SessionId,
                    UserId = e.UserId,
                    AgentId = targetAgentId,
                    DocumentId = targetDocumentId.ToString("D"),
                    FileName = e.FileName,
                    ChunkIndex = e.ChunkIndex,
                    Page = e.Page,
                    Position = e.Position,
                    Content = e.Content,
                    CreatedAt = DateTime.UtcNow,
                    Embedding = e.Embedding
                }).ToList();

                await _collection.UpsertAsync(copies, cancellationToken);
                copied += page.Count;
            }

            return copied;
        }

        public Task DeleteDocumentAsync(ChatDocumentScope scope, Guid documentId, CancellationToken cancellationToken = default) =>
            DeleteMatchingAsync(ChatDocumentFilters.ForScope(scope, [documentId]), cancellationToken);

        public Task DeleteAgentDocumentsAsync(ChatDocumentScope scope, IReadOnlyCollection<int> agentIds, CancellationToken cancellationToken = default) =>
            agentIds.Count == 0 ? Task.CompletedTask : DeleteMatchingAsync(ChatDocumentFilters.ForAgents(scope, agentIds), cancellationToken);

        // Deletes by the keys of the records the filter matches, a page at a time: a document or chat can hold more
        // records than a single delete-by-filter reads (10,000), and every page is found with the scope's filter.
        private async Task DeleteMatchingAsync(Expression<Func<ChatDocumentChunkEntry, bool>> filter, CancellationToken cancellationToken)
        {
            await EnsureCollectionAsync(cancellationToken);

            while (true)
            {
                var keys = new List<string>();
                await foreach (var entry in _collection.GetAsync(filter, PageSize * 2, cancellationToken: cancellationToken))
                    keys.Add(entry.Id);

                if (keys.Count == 0)
                    return;

                await _collection.DeleteAsync(keys, cancellationToken);
            }
        }

        private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
        {
            if (_ensured)
                return;

            await _ensureLock.WaitAsync(cancellationToken);
            try
            {
                if (_ensured)
                    return;

                await _collection.EnsureCollectionExistsAsync(cancellationToken);
                _ensured = true;
            }
            finally
            {
                _ensureLock.Release();
            }
        }

        internal static string Key(Guid documentId, int chunkIndex) => $"{documentId:D}_{chunkIndex}";

        private static ChatDocumentPassage ToPassage(ChatDocumentChunkEntry entry, double score) =>
            new(Guid.Parse(entry.DocumentId), entry.ChunkIndex, entry.FileName, entry.Page, entry.Position, entry.Content, score);
    }
}
