namespace AMAIS.WKFLO.Common.AI.Application.Services.Documents
{
    /// <summary>The outcome of indexing a document; the values the short history message and its metadata carry.</summary>
    public static class ChatDocumentIndexStatus
    {
        /// <summary>Every chunk is stored.</summary>
        public const string Ready = "ready";

        /// <summary>Only some chunks are stored (chunk cap or time budget reached); they are spread evenly over the document.</summary>
        public const string Partial = "partial";

        /// <summary>No chunk could be stored; the document can only be read through its summary or excerpt.</summary>
        public const string Failed = "failed";
    }

    /// <summary>
    /// A large document attached to a chat: what the history keeps instead of its text. <see cref="ChunkCount"/> is the
    /// number of chunks the whole text splits into; <see cref="IndexedChunkCount"/> how many of them are stored.
    /// </summary>
    public sealed class AttachedDocumentInfo
    {
        public Guid DocumentId { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public int FileSize { get; set; }
        public int CharCount { get; set; }
        public int ChunkCount { get; set; }
        public int IndexedChunkCount { get; set; }
        public string IndexStatus { get; set; }

        /// <summary>A short summary built once from a sampled excerpt; null when it could not be built.</summary>
        public string Summary { get; set; }

        /// <summary>The sampled excerpt, kept only when the document could not be indexed (<see cref="ChatDocumentIndexStatus.Failed"/>).</summary>
        public string Excerpt { get; set; }

        public int? PageCount { get; set; }
    }
}
