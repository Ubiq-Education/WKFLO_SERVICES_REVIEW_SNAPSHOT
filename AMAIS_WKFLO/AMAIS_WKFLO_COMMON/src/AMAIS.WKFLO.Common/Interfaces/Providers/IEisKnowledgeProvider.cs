namespace AMAIS.WKFLO.Common.Interfaces.Providers
{
    public interface IEisKnowledgeProvider
    {
        Task<IReadOnlyList<EisDocument>> RetrieveAsync(
            string tenantId,
            string question,
            EisKnowledgeQueryOptions options,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Options controlling a single <see cref="IEisKnowledgeProvider.RetrieveAsync"/> call, including
    /// the optional branch scope and time-context (extracted from arbitrary natural language by
    /// <see cref="AI.Application.Plugins.Amais.Knowledge.TimeContextExtractor"/>) used to filter/rank
    /// results.
    /// </summary>
    public class EisKnowledgeQueryOptions
    {
        public string BranchId { get; set; }

        /// <summary>Current date/time in UTC, used as the "now" reference point (e.g. by callers resolving a time-context range). Not itself used as a query filter.</summary>
        public DateTimeOffset? AsOfUtc { get; set; }

        /// <summary>Inclusive lower bound (UTC) an event's end date must fall on/after to be included. Null means unbounded below.</summary>
        public DateTimeOffset? EventDateRangeStartUtc { get; set; }

        /// <summary>Inclusive upper bound (UTC) an event's end date must fall on/before to be included. Null means unbounded above.</summary>
        public DateTimeOffset? EventDateRangeEndUtc { get; set; }

        /// <summary>
        /// When the user is asking for a specific chronological extreme within the (possibly unbounded)
        /// date range - "the next event", "the first event of September", "the last event of the year" -
        /// this is <c>true</c> for earliest-first ("next"/"first") or <c>false</c> for latest-first
        /// ("most recent"/"last"). <c>null</c> means no such extremal preference was detected (a general
        /// "what happened in August"-style listing), in which case relevance ranking is used instead - see
        /// <see cref="Infrastructure.Providers.EisKnowledgeProvider.BuildSort"/>.
        /// </summary>
        public bool? EventDateSortAscending { get; set; }
    }

    public class EisDocument
    {
        public string Content { get; set; }
        public string Name { get; set; }
        public string Links { get; set; }
        public DateTimeOffset? EventEndDate { get; set; }
        public DateTimeOffset? ContentUpdatedAt { get; set; }
    }
}
