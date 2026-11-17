using AMAIS.WKFLO.Common.Models.Documents;

namespace AMAIS.WKFLO.Common.Interfaces.Providers
{
    /// <summary>
    /// Bug 8189 / Task 8219: the chunks of large chat attachments, in one shared vector collection. Every operation that
    /// reads or deletes takes a <see cref="ChatDocumentScope"/> and filters by tenant, session and user (and document ids
    /// where given); there is no way to read or delete by document id alone.
    /// </summary>
    public interface IChatDocumentStore
    {
        Task UpsertAsync(ChatDocumentScope scope, IReadOnlyList<ChatDocumentChunk> chunks, CancellationToken cancellationToken = default);

        /// <summary>The chunks closest to <paramref name="queryEmbedding"/> with a similarity of at least <paramref name="minScore"/>, best first.</summary>
        /// <param name="fileName">When given, only that file's chunks.</param>
        Task<IReadOnlyList<ChatDocumentPassage>> SearchAsync(ChatDocumentScope scope, IReadOnlyCollection<Guid> documentIds, ReadOnlyMemory<float> queryEmbedding,
            int top, double minScore, string fileName = null, CancellationToken cancellationToken = default);

        /// <summary>The stored chunks of one document with these indices (missing ones are left out), in chunk order.</summary>
        Task<IReadOnlyList<ChatDocumentPassage>> GetChunksAsync(ChatDocumentScope scope, Guid documentId, IReadOnlyCollection<int> chunkIndexes,
            CancellationToken cancellationToken = default);

        /// <summary>Copies a document's chunks to <paramref name="targetDocumentId"/> inside the same scope (forwarding to another agent). Returns the chunk count.</summary>
        Task<int> CopyDocumentAsync(ChatDocumentScope scope, Guid sourceDocumentId, Guid targetDocumentId, int targetAgentId, CancellationToken cancellationToken = default);

        Task DeleteDocumentAsync(ChatDocumentScope scope, Guid documentId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the documents of the chat that belong to these agents only. A hand-off runs other agents in the same session, so
        /// removing one agent's chat must not remove the documents of the others.
        /// </summary>
        Task DeleteAgentDocumentsAsync(ChatDocumentScope scope, IReadOnlyCollection<int> agentIds, CancellationToken cancellationToken = default);
    }
}
