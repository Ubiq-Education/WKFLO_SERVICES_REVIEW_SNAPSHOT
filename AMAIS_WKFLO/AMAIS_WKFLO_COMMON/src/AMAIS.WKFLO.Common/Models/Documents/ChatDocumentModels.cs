namespace AMAIS.WKFLO.Common.Models.Documents
{
    /// <summary>
    /// Whose documents a store call may touch. Built on the server from the turn's tenant, session and user, never from
    /// model output or the request body; every store query and delete carries all three.
    /// </summary>
    /// <param name="UserId">The user's Guid text; <see cref="Guid.Empty"/> for anonymous/public chats (same convention as the chat blob path).</param>
    public sealed record ChatDocumentScope(string TenantName, string SessionId, string UserId)
    {
        public static ChatDocumentScope For(string tenantName, string sessionId, string userId) =>
            new(tenantName, sessionId, string.IsNullOrWhiteSpace(userId) ? Guid.Empty.ToString() : userId);
    }

    /// <summary>One chunk of a document to store, with its embedding.</summary>
    public sealed record ChatDocumentChunk(Guid DocumentId, int ChunkIndex, int AgentId, string FileName, int? Page, int Position,
        string Content, ReadOnlyMemory<float> Embedding);

    /// <summary>A stored chunk read back; <see cref="Score"/> is the cosine similarity for a search, 0 otherwise.</summary>
    public sealed record ChatDocumentPassage(Guid DocumentId, int ChunkIndex, string FileName, int? Page, int Position, string Content, double Score);
}
