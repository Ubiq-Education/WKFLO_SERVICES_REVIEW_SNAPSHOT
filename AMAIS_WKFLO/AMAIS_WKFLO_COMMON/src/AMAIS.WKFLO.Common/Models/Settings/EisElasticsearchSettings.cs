namespace AMAIS.WKFLO.Common.Models.Settings
{
    public class EisElasticsearchSettings
    {
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public int TopK { get; set; }

        /// <summary>
        /// Elasticsearch metadata field carrying a calendar event's end date (ISO-8601 UTC), used to
        /// exclude past events when the user is asking about upcoming events. AMAIS_CHATBOT indexes this
        /// nested under "metadata" (not as a top-level field), confirmed by inspecting a real ingested
        /// document's mapping - a top-level "event_end_date" simply doesn't exist, so a query filtering on
        /// it would never match, silently falling through the "missing field passes through" branch and
        /// letting every document through regardless of date. Still configurable in case ingestion's field
        /// path ever changes again.
        /// </summary>
        public string EventEndDateField { get; set; } = "metadata.event_end_date";

        /// <summary>
        /// Elasticsearch metadata field carrying the best-available freshness signal for a document
        /// (ISO-8601 UTC), used to rank the most recently updated content first for admissions-style
        /// queries. Same "metadata."-nested path as EventEndDateField above, for the same reason.
        /// </summary>
        public string ContentUpdatedAtField { get; set; } = "metadata.content_updated_at";
    }
}
