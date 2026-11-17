using AMAIS.WKFLO.Common.Infrastructure.Providers.Clients;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Settings;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    public class EisKnowledgeProvider : IEisKnowledgeProvider
    {
        private readonly EisElasticsearchClient _client;
        private readonly EisElasticsearchSettings _eisElasticsearchSettings;
        private readonly ILogger<EisKnowledgeProvider> _logger;

        public EisKnowledgeProvider(EisElasticsearchClient client, IOptions<EisElasticsearchSettings> settings, ILogger<EisKnowledgeProvider> logger)
        {
            _client = client;
            _eisElasticsearchSettings = settings.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<EisDocument>> RetrieveAsync(
            string tenantId,
            string question,
            EisKnowledgeQueryOptions options,
            CancellationToken cancellationToken = default)
        {
            options ??= new EisKnowledgeQueryOptions();

            var indexName = $"eis-index-{tenantId.ToLower()}";

            try
            {
                var query = BuildQuery(question, options, _eisElasticsearchSettings);
                var sort = BuildSort(options, _eisElasticsearchSettings);

                var response = await _client.SearchAsync<EisDocumentDto>(s =>
                {
                    s.Indices(indexName).Size(_eisElasticsearchSettings.TopK).Sort(sort);
                    s.Query(query);
                }, cancellationToken);

                if (!response.IsValidResponse)
                {
                    _logger.LogWarning("EIS knowledge retrieval returned invalid response for index {Index}: {DebugInfo}",
                        indexName, response.DebugInformation);
                    return [];
                }

                return response.Hits
                    .Where(h => h.Source != null)
                    .Select(h => new EisDocument
                    {
                        Content = h.Source!.Text ?? string.Empty,
                        Name = h.Source!.Metadata?.Name ?? h.Source!.Metadata?.Title ?? h.Source!.Metadata?.Filename ?? string.Empty,
                        Links = h.Source!.Metadata?.Links ?? string.Empty,
                        EventEndDate = h.Source!.Metadata?.EventEndDate,
                        ContentUpdatedAt = h.Source!.Metadata?.ContentUpdatedAt
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EIS knowledge retrieval failed for index {Index}", indexName);
                return [];
            }
        }

        /// <summary>
        /// Builds the Elasticsearch bool query: the existing relevance (ELSER text-expansion) clause and
        /// branch filter, plus an additional filter that constrains an event's end date to the requested
        /// range whenever either <see cref="EisKnowledgeQueryOptions.EventDateRangeStartUtc"/> or
        /// <see cref="EisKnowledgeQueryOptions.EventDateRangeEndUtc"/> is set (only the non-null bound(s)
        /// are applied). Documents missing the event-end-date field are never excluded by the date filter
        /// (mirrors the existing branch filter's "missing field passes through" pattern) so non-event
        /// content matched on relevance isn't wrongly filtered out - this also means non-event documents
        /// are effectively unaffected by any time-context range, since they simply have no such field.
        /// </summary>
        internal static Query BuildQuery(string question, EisKnowledgeQueryOptions options, EisElasticsearchSettings settings)
        {
            var boolQuery = new BoolQuery
            {
                Must = new List<Query>
                {
                    new TextExpansionQuery
                    {
                        Field = "vector.tokens",
                        ModelId = ".elser-2-elastic",
                        ModelText = question
                    }
                }
            };

            var filters = new List<Query>();

            if (!string.IsNullOrEmpty(options.BranchId))
            {
                filters.Add(new BoolQuery
                {
                    Should = new List<Query>
                    {
                        new TermQuery { Field = "branch", Value = options.BranchId },
                        new BoolQuery { MustNot = new List<Query> { new ExistsQuery { Field = "branch" } } }
                    }
                });
            }

            if (options.EventDateRangeStartUtc.HasValue || options.EventDateRangeEndUtc.HasValue)
            {
                var eventEndDateField = settings.EventEndDateField;

                var dateRangeQuery = new DateRangeQuery { Field = eventEndDateField };

                if (options.EventDateRangeStartUtc.HasValue)
                {
                    dateRangeQuery.Gte = options.EventDateRangeStartUtc.Value.UtcDateTime;
                }

                if (options.EventDateRangeEndUtc.HasValue)
                {
                    dateRangeQuery.Lte = options.EventDateRangeEndUtc.Value.UtcDateTime;
                }

                filters.Add(new BoolQuery
                {
                    Should = new List<Query>
                    {
                        dateRangeQuery,
                        new BoolQuery { MustNot = new List<Query> { new ExistsQuery { Field = eventEndDateField } } }
                    }
                });
            }

            if (filters.Count > 0)
            {
                boolQuery.Filter = filters;
            }

            return boolQuery;
        }

        /// <summary>
        /// Builds the sort order. When <see cref="EisKnowledgeQueryOptions.EventDateSortAscending"/> is
        /// set - the user asked for a specific chronological extreme ("the next event", "the first event
        /// of September", "the last event of the year") rather than a general listing - the event date
        /// itself is sorted first (ascending = earliest/next first, descending = latest/most recent
        /// first), ahead of relevance. This matters because a question like "what's the next event?"
        /// carries no distinguishing text signal to rank candidates on, so relying on ELSER relevance
        /// alone can surface an event months away over one happening tomorrow - see the live-test finding
        /// that motivated this. Documents without an event date (non-event content, still eligible via
        /// BuildQuery's "missing field passes through" filter) always sort last, via
        /// <c>Missing = "_last"</c>, regardless of direction, so they never crowd out real dated events
        /// for this primary key. Every field sort also sets <c>UnmappedType = FieldType.Date</c>, so a
        /// tenant index that hasn't yet had its mapping synced with the new date fields (see
        /// chatbot-data's ensure_index_mapping_is_up_to_date) degrades to treating the field as present-
        /// but-empty (subject to the same <c>Missing</c> handling) instead of throwing a shard failure that
        /// would return zero knowledge results.
        ///
        /// When no such extremal preference was detected - a general "what happened in August"-style
        /// listing, whether the date range is open, closed, or absent entirely - relevance score stays
        /// first, unaffected by this change and matching already-verified behavior. This also avoids a
        /// worse failure mode: sorting a general listing by event date would let TopK silently truncate
        /// to only the chronologically-earliest slice of a wide range instead of the most relevant spread
        /// of results.
        ///
        /// In every case, relevance score and then "most recently updated" are appended as further
        /// tiebreakers, so recency never overrides relevance/date-proximity - it only breaks ties.
        /// </summary>
        internal static ICollection<SortOptions> BuildSort(EisKnowledgeQueryOptions options, EisElasticsearchSettings settings)
        {
            var sort = new List<SortOptions>();

            if (options.EventDateSortAscending.HasValue)
            {
                var order = options.EventDateSortAscending.Value ? SortOrder.Asc : SortOrder.Desc;
                sort.Add(new SortOptions { Field = new FieldSort { Field = settings.EventEndDateField, Order = order, Missing = "_last", UnmappedType = Elastic.Clients.Elasticsearch.Mapping.FieldType.Date } });
            }

            sort.Add(new SortOptions { Score = new ScoreSort { Order = SortOrder.Desc } });
            sort.Add(new SortOptions { Field = new FieldSort { Field = settings.ContentUpdatedAtField, Order = SortOrder.Desc, UnmappedType = Elastic.Clients.Elasticsearch.Mapping.FieldType.Date } });

            return sort;
        }

        private class EisDocumentDto
        {
            [JsonPropertyName("text")]
            public string Text { get; set; }

            [JsonPropertyName("metadata")]
            public EisMetadataDto Metadata { get; set; }
        }

        // Note: JsonPropertyName attributes are compile-time constants, so unlike the query/sort field
        // names in BuildQuery/BuildSort (driven by EisElasticsearchSettings), these two property names
        // are not runtime-configurable. They match the settings' default field names
        // (event_end_date/content_updated_at); if ingestion's final field names differ from those
        // defaults, both this DTO and the settings defaults need to be updated together.
        private class EisMetadataDto
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("filename")]
            public string Filename { get; set; }

            [JsonPropertyName("links")]
            public string Links { get; set; }

            [JsonPropertyName("event_end_date")]
            public DateTimeOffset? EventEndDate { get; set; }

            [JsonPropertyName("content_updated_at")]
            public DateTimeOffset? ContentUpdatedAt { get; set; }
        }
    }
}
