using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.Documents
{
    /// <summary>
    /// An in-memory <see cref="IChatDocumentStore"/> that applies the same isolation as the real one (tenant, session, user,
    /// document ids) and records the scope of every call, so tests can check what a caller asked for and what it could see.
    /// </summary>
    public sealed class InMemoryChatDocumentStore : IChatDocumentStore
    {
        private sealed record Row(ChatDocumentScope Scope, ChatDocumentChunk Chunk, DateTime CreatedAt);

        private readonly object _gate = new();
        private readonly List<Row> _rows = [];

        public List<(string Method, ChatDocumentScope Scope)> Calls { get; } = [];
        public int UpsertCalls { get; private set; }
        public List<int> UpsertBatchSizes { get; } = [];
        public List<int> FirstChunkOfEachUpsert { get; } = [];

        /// <summary>Makes every upsert throw, e.g. a database that is down.</summary>
        public Exception FailUpserts { get; set; }

        public int ChunkCount(Guid documentId)
        {
            lock (_gate) return _rows.Count(r => r.Chunk.DocumentId == documentId);
        }

        public int TotalChunks
        {
            get { lock (_gate) return _rows.Count; }
        }

        public void SetCreatedAt(Func<ChatDocumentScope, bool> predicate, DateTime createdAt)
        {
            lock (_gate)
            {
                for (var i = 0; i < _rows.Count; i++)
                {
                    if (predicate(_rows[i].Scope))
                        _rows[i] = _rows[i] with { CreatedAt = createdAt };
                }
            }
        }

        public Task UpsertAsync(ChatDocumentScope scope, IReadOnlyList<ChatDocumentChunk> chunks, CancellationToken cancellationToken = default)
        {
            if (FailUpserts != null)
                throw FailUpserts;

            lock (_gate)
            {
                Calls.Add((nameof(UpsertAsync), scope));
                UpsertCalls++;
                UpsertBatchSizes.Add(chunks.Count);
                FirstChunkOfEachUpsert.Add(chunks[0].ChunkIndex);
                foreach (var chunk in chunks)
                {
                    _rows.RemoveAll(r => r.Scope == scope && r.Chunk.DocumentId == chunk.DocumentId && r.Chunk.ChunkIndex == chunk.ChunkIndex);
                    _rows.Add(new Row(scope, chunk, DateTime.UtcNow));
                }
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatDocumentPassage>> SearchAsync(ChatDocumentScope scope, IReadOnlyCollection<Guid> documentIds, ReadOnlyMemory<float> queryEmbedding,
            int top, double minScore, string fileName = null, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Calls.Add((nameof(SearchAsync), scope));
                var found = _rows
                    .Where(r => r.Scope == scope && documentIds.Contains(r.Chunk.DocumentId) && (fileName == null || r.Chunk.FileName == fileName))
                    .Select(r => (Row: r, Score: Cosine(queryEmbedding.Span, r.Chunk.Embedding.Span)))
                    .Where(x => x.Score >= minScore)
                    .OrderByDescending(x => x.Score)
                    .Take(top)
                    .Select(x => ToPassage(x.Row.Chunk, x.Score))
                    .ToList();
                return Task.FromResult<IReadOnlyList<ChatDocumentPassage>>(found);
            }
        }

        public Task<IReadOnlyList<ChatDocumentPassage>> GetChunksAsync(ChatDocumentScope scope, Guid documentId, IReadOnlyCollection<int> chunkIndexes,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Calls.Add((nameof(GetChunksAsync), scope));
                var found = _rows
                    .Where(r => r.Scope == scope && r.Chunk.DocumentId == documentId && chunkIndexes.Contains(r.Chunk.ChunkIndex))
                    .OrderBy(r => r.Chunk.ChunkIndex)
                    .Select(r => ToPassage(r.Chunk, 0))
                    .ToList();
                return Task.FromResult<IReadOnlyList<ChatDocumentPassage>>(found);
            }
        }

        public Task<int> CopyDocumentAsync(ChatDocumentScope scope, Guid sourceDocumentId, Guid targetDocumentId, int targetAgentId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Calls.Add((nameof(CopyDocumentAsync), scope));
                var source = _rows.Where(r => r.Scope == scope && r.Chunk.DocumentId == sourceDocumentId).ToList();
                foreach (var row in source)
                    _rows.Add(new Row(scope, row.Chunk with { DocumentId = targetDocumentId, AgentId = targetAgentId }, DateTime.UtcNow));
                return Task.FromResult(source.Count);
            }
        }

        public Task DeleteDocumentAsync(ChatDocumentScope scope, Guid documentId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Calls.Add((nameof(DeleteDocumentAsync), scope));
                _rows.RemoveAll(r => r.Scope == scope && r.Chunk.DocumentId == documentId);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAgentDocumentsAsync(ChatDocumentScope scope, IReadOnlyCollection<int> agentIds, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Calls.Add((nameof(DeleteAgentDocumentsAsync), scope));
                _rows.RemoveAll(r => r.Scope == scope && agentIds.Contains(r.Chunk.AgentId));
            }

            return Task.CompletedTask;
        }

        private static ChatDocumentPassage ToPassage(ChatDocumentChunk c, double score) =>
            new(c.DocumentId, c.ChunkIndex, c.FileName, c.Page, c.Position, c.Content, score);

        private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            double dot = 0, na = 0, nb = 0;
            for (var i = 0; i < a.Length && i < b.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }

            return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }
    }
}
