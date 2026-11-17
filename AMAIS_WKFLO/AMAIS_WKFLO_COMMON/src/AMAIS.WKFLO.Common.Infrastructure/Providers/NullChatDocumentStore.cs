using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Documents;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    /// <summary>
    /// An <see cref="IChatDocumentStore"/> that stores nothing and finds nothing. For test hosts (functional test fixtures)
    /// that must not reach the Postgres database.
    /// </summary>
    public class NullChatDocumentStore : IChatDocumentStore
    {
        public Task UpsertAsync(ChatDocumentScope scope, IReadOnlyList<ChatDocumentChunk> chunks, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ChatDocumentPassage>> SearchAsync(ChatDocumentScope scope, IReadOnlyCollection<Guid> documentIds, ReadOnlyMemory<float> queryEmbedding,
            int top, double minScore, string fileName = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatDocumentPassage>>([]);

        public Task<IReadOnlyList<ChatDocumentPassage>> GetChunksAsync(ChatDocumentScope scope, Guid documentId, IReadOnlyCollection<int> chunkIndexes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatDocumentPassage>>([]);

        public Task<int> CopyDocumentAsync(ChatDocumentScope scope, Guid sourceDocumentId, Guid targetDocumentId, int targetAgentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task DeleteDocumentAsync(ChatDocumentScope scope, Guid documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAgentDocumentsAsync(ChatDocumentScope scope, IReadOnlyCollection<int> agentIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
