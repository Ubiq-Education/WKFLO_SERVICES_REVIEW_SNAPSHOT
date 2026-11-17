namespace AMAIS.WKFLO.Common.Models.Settings
{
    /// <summary>
    /// Bug 8189 / Task 8219: settings for large chat attachments. A document whose extracted text is longer than
    /// <see cref="InlineMaxChars"/> is not put in the chat history: its text is split into chunks, embedded and stored
    /// in one shared Postgres vector collection, and each turn only receives the passages relevant to the question.
    /// </summary>
    public class ChatDocumentSettings
    {
        /// <summary>Documents with at most this many extracted characters stay inline in the history, as before.</summary>
        public int InlineMaxChars { get; set; } = 50_000;

        /// <summary>Name of the shared vector collection (table); created on first use.</summary>
        public string CollectionName { get; set; } = "chat_documents";

        public int ChunkTargetChars { get; set; } = 1_500;
        public int ChunkMaxChars { get; set; } = 3_000;

        public int MaxIndexedChunksPerDocument { get; set; } = 2_000;
        public int EmbeddingBatchSize { get; set; } = 64;
        public int EmbeddingParallelism { get; set; } = 2;
        public int IndexingTimeBudgetSeconds { get; set; } = 90;

        /// <summary>Size of the sampled excerpt the summary is built from.</summary>
        public int ExcerptChars { get; set; } = 200_000;
        public int SummaryMaxChars { get; set; } = 1_500;

        public int RetrievalTopK { get; set; } = 8;
        public double RetrievalMinScore { get; set; } = 0.25;
        public int RetrievalMaxChars { get; set; } = 16_000;

        /// <summary>When no passage is relevant, the summary plus an excerpt of up to this many characters is supplied.</summary>
        public int FallbackExcerptChars { get; set; } = 40_000;

        /// <summary>Most documents of a session considered in one turn.</summary>
        public int MaxDocumentsPerTurn { get; set; } = 10;

        public int SearchToolMaxPassages { get; set; } = 8;
        public int SearchToolMaxChars { get; set; } = 12_000;
    }
}
