using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Settings;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using System;
using System.Linq;

namespace AMAIS.WKFLO.AI.UnitTests.Providers
{
    /// <summary>
    /// First-ever coverage of EisKnowledgeProvider's query construction, exercised via the internal
    /// BuildQuery/BuildSort seams (see AssemblyInfo.cs InternalsVisibleTo).
    /// </summary>
    public class EisKnowledgeProviderQueryBuilderTests
    {
        private static EisElasticsearchSettings DefaultSettings() => new() { TopK = 5 };

        // ── BuildQuery: no range (regression safety net) ─────────────────────

        [Fact]
        public void BuildQuery_WithNoRangeAndNoBranch_ReproducesExistingQueryShape()
        {
            var options = new EisKnowledgeQueryOptions();

            var query = EisKnowledgeProvider.BuildQuery("what time is pickup", options, DefaultSettings());

            Assert.NotNull(query.Bool);
            var must = Assert.Single(query.Bool!.Must!);
            // BuildQuery still emits a text_expansion query; Query.TextExpansion is the only typed accessor for it.
#pragma warning disable CS0618
            var textExpansion = must.TextExpansion;
#pragma warning restore CS0618
            Assert.NotNull(textExpansion);
            Assert.Equal("what time is pickup", textExpansion!.ModelText);
            Assert.Equal(".elser-2-elastic", textExpansion.ModelId);
            Assert.Equal("vector.tokens", textExpansion.Field!.Name);
            Assert.Null(query.Bool!.Filter);
        }

        // ── BuildQuery: branch filter ─────────────────────────────────────────

        [Fact]
        public void BuildQuery_WithBranchId_AddsBranchFilterWithMissingFieldFallback()
        {
            var options = new EisKnowledgeQueryOptions { BranchId = "branch-1" };

            var query = EisKnowledgeProvider.BuildQuery("question", options, DefaultSettings());

            var filter = Assert.Single(query.Bool!.Filter!);
            Assert.NotNull(filter.Bool);
            Assert.Equal(2, filter.Bool!.Should!.Count);

            var termClause = filter.Bool!.Should!.First();
            Assert.NotNull(termClause.Term);
            Assert.Equal("branch", termClause.Term!.Field!.Name);
            Assert.Equal("branch-1", termClause.Term!.Value.ToString());

            var missingFieldClause = filter.Bool!.Should!.Last();
            Assert.NotNull(missingFieldClause.Bool);
            var mustNot = Assert.Single(missingFieldClause.Bool!.MustNot!);
            Assert.Equal("branch", mustNot.Exists!.Field!.Name);
        }

        [Fact]
        public void BuildQuery_WithoutBranchId_DoesNotAddBranchFilter()
        {
            var options = new EisKnowledgeQueryOptions { BranchId = null };

            var query = EisKnowledgeProvider.BuildQuery("question", options, DefaultSettings());

            Assert.Null(query.Bool!.Filter);
        }

        // ── BuildQuery: event date range filter ──────────────────────────────

        [Fact]
        public void BuildQuery_WithStartOnly_AddsGteRangeFilterWithMissingFieldFallback()
        {
            var start = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
            var options = new EisKnowledgeQueryOptions { EventDateRangeStartUtc = start };

            var query = EisKnowledgeProvider.BuildQuery("question", options, DefaultSettings());

            var filter = Assert.Single(query.Bool!.Filter!);
            Assert.Equal(2, filter.Bool!.Should!.Count);

            var rangeClause = filter.Bool!.Should!.First();
            var dateRange = Assert.IsType<DateRangeQuery>(rangeClause.Range);
            Assert.Equal("metadata.event_end_date", dateRange.Field!.Name);
            Assert.Equal("2026-07-30T00:00:00Z", dateRange.Gte!.ToString());
            Assert.Null(dateRange.Lte);

            var missingFieldClause = filter.Bool!.Should!.Last();
            var mustNot = Assert.Single(missingFieldClause.Bool!.MustNot!);
            Assert.Equal("metadata.event_end_date", mustNot.Exists!.Field!.Name);
        }

        [Fact]
        public void BuildQuery_WithEndOnly_AddsLteRangeFilterWithMissingFieldFallback()
        {
            var end = new DateTimeOffset(2026, 7, 30, 23, 59, 59, TimeSpan.Zero);
            var options = new EisKnowledgeQueryOptions { EventDateRangeEndUtc = end };

            var query = EisKnowledgeProvider.BuildQuery("question", options, DefaultSettings());

            var filter = Assert.Single(query.Bool!.Filter!);
            var rangeClause = filter.Bool!.Should!.First();
            var dateRange = Assert.IsType<DateRangeQuery>(rangeClause.Range);
            Assert.Equal("metadata.event_end_date", dateRange.Field!.Name);
            Assert.Null(dateRange.Gte);
            Assert.Equal("2026-07-30T23:59:59Z", dateRange.Lte!.ToString());
        }

        [Fact]
        public void BuildQuery_WithStartAndEnd_AddsBothBoundsOnSameRangeFilter()
        {
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);
            var options = new EisKnowledgeQueryOptions { EventDateRangeStartUtc = start, EventDateRangeEndUtc = end };

            var query = EisKnowledgeProvider.BuildQuery("question", options, DefaultSettings());

            var filter = Assert.Single(query.Bool!.Filter!);
            var rangeClause = filter.Bool!.Should!.First();
            var dateRange = Assert.IsType<DateRangeQuery>(rangeClause.Range);
            Assert.Equal("2026-01-01T00:00:00Z", dateRange.Gte!.ToString());
            Assert.Equal("2026-01-31T23:59:59Z", dateRange.Lte!.ToString());
        }

        [Fact]
        public void BuildQuery_WithNoRangeBounds_DoesNotAddDateFilter()
        {
            var options = new EisKnowledgeQueryOptions { EventDateRangeStartUtc = null, EventDateRangeEndUtc = null };

            var query = EisKnowledgeProvider.BuildQuery("question", options, DefaultSettings());

            Assert.Null(query.Bool!.Filter);
        }

        [Fact]
        public void BuildQuery_WithRange_UsesConfiguredFieldName()
        {
            var start = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
            var options = new EisKnowledgeQueryOptions { EventDateRangeStartUtc = start };
            var settings = new EisElasticsearchSettings { TopK = 5, EventEndDateField = "custom_event_end" };

            var query = EisKnowledgeProvider.BuildQuery("question", options, settings);

            var filter = Assert.Single(query.Bool!.Filter!);
            var rangeClause = filter.Bool!.Should!.First();
            var dateRange = Assert.IsType<DateRangeQuery>(rangeClause.Range);
            Assert.Equal("custom_event_end", dateRange.Field!.Name);
        }

        // ── BuildQuery: branch filter + date filter compose together ────────

        [Fact]
        public void BuildQuery_WithBranchAndDateRange_ComposesBothFiltersWithoutClobbering()
        {
            var start = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
            var options = new EisKnowledgeQueryOptions
            {
                BranchId = "branch-1",
                EventDateRangeStartUtc = start
            };

            var query = EisKnowledgeProvider.BuildQuery("question", options, DefaultSettings());

            Assert.Equal(2, query.Bool!.Filter!.Count);

            var branchFilter = query.Bool!.Filter!.First();
            Assert.Equal("branch", branchFilter.Bool!.Should!.First().Term!.Field!.Name);

            var dateFilter = query.Bool!.Filter!.Last();
            var dateRange = Assert.IsType<DateRangeQuery>(dateFilter.Bool!.Should!.First().Range);
            Assert.Equal("metadata.event_end_date", dateRange.Field!.Name);
        }

        // ── BuildSort: always unconditional ──────────────────────────────────

        [Fact]
        public void BuildSort_AlwaysAppendsSecondarySortByContentUpdatedAtDescending()
        {
            var options = new EisKnowledgeQueryOptions();

            var sort = EisKnowledgeProvider.BuildSort(options, DefaultSettings());

            Assert.Equal(2, sort.Count);

            var scoreSort = sort.First();
            Assert.NotNull(scoreSort.Score);
            Assert.Equal(SortOrder.Desc, scoreSort.Score!.Order);

            var recencySort = sort.Last();
            Assert.NotNull(recencySort.Field);
            Assert.Equal("metadata.content_updated_at", recencySort.Field!.Field!.Name);
            Assert.Equal(SortOrder.Desc, recencySort.Field!.Order);
        }

        [Fact]
        public void BuildSort_WithDateRangeSet_StillAppendsSecondarySort()
        {
            var options = new EisKnowledgeQueryOptions { EventDateRangeStartUtc = DateTimeOffset.UtcNow };

            var sort = EisKnowledgeProvider.BuildSort(options, DefaultSettings());

            Assert.Equal(2, sort.Count);
        }

        [Fact]
        public void BuildSort_UsesConfiguredFieldName()
        {
            var options = new EisKnowledgeQueryOptions();
            var settings = new EisElasticsearchSettings { TopK = 5, ContentUpdatedAtField = "custom_updated_at" };

            var sort = EisKnowledgeProvider.BuildSort(options, settings);

            var recencySort = sort.Last();
            Assert.Equal("custom_updated_at", recencySort.Field!.Field!.Name);
        }

        [Fact]
        public void BuildSort_ScoreSortIsAlwaysFirst()
        {
            var options = new EisKnowledgeQueryOptions();

            var sort = EisKnowledgeProvider.BuildSort(options, DefaultSettings());

            Assert.NotNull(sort.First().Score);
        }

        [Fact]
        public void BuildSort_ContentUpdatedAtSort_SetsUnmappedTypeToAvoidShardFailureOnStaleIndices()
        {
            // Regression for a Copilot PR-review finding: this sort is unconditional on every query, so a
            // tenant index that hasn't yet had its mapping synced with the content_updated_at field would
            // otherwise throw a shard failure (returning zero knowledge results) instead of degrading
            // gracefully.
            var options = new EisKnowledgeQueryOptions();

            var sort = EisKnowledgeProvider.BuildSort(options, DefaultSettings());

            Assert.Equal(Elastic.Clients.Elasticsearch.Mapping.FieldType.Date, sort.Last().Field!.UnmappedType);
        }

        // ── BuildSort: extremal (earliest/latest) preference ────────────────

        [Fact]
        public void BuildSort_WithEventDateSortAscendingTrue_SortsEventDateFirstAscendingWithMissingLast()
        {
            var options = new EisKnowledgeQueryOptions { EventDateSortAscending = true };

            var sort = EisKnowledgeProvider.BuildSort(options, DefaultSettings());

            Assert.Equal(3, sort.Count);
            var dateSort = sort.First();
            Assert.NotNull(dateSort.Field);
            Assert.Equal("metadata.event_end_date", dateSort.Field!.Field!.Name);
            Assert.Equal(SortOrder.Asc, dateSort.Field!.Order);
            Assert.Equal("_last", dateSort.Field!.Missing!.ToString());

            Assert.NotNull(sort.ElementAt(1).Score);
            Assert.Equal("metadata.content_updated_at", sort.Last().Field!.Field!.Name);
            Assert.Equal(Elastic.Clients.Elasticsearch.Mapping.FieldType.Date, dateSort.Field!.UnmappedType);
        }

        [Fact]
        public void BuildSort_WithEventDateSortAscendingFalse_SortsEventDateFirstDescending()
        {
            var options = new EisKnowledgeQueryOptions { EventDateSortAscending = false };

            var sort = EisKnowledgeProvider.BuildSort(options, DefaultSettings());

            Assert.Equal(3, sort.Count);
            var dateSort = sort.First();
            Assert.Equal("metadata.event_end_date", dateSort.Field!.Field!.Name);
            Assert.Equal(SortOrder.Desc, dateSort.Field!.Order);
        }

        [Fact]
        public void BuildSort_WithEventDateSortAscendingNull_DoesNotAddDateSort()
        {
            var options = new EisKnowledgeQueryOptions { EventDateSortAscending = null };

            var sort = EisKnowledgeProvider.BuildSort(options, DefaultSettings());

            Assert.Equal(2, sort.Count);
        }

        [Fact]
        public void BuildSort_WithEventDateSortAscending_UsesConfiguredEventEndDateFieldName()
        {
            var options = new EisKnowledgeQueryOptions { EventDateSortAscending = true };
            var settings = new EisElasticsearchSettings { TopK = 5, EventEndDateField = "custom_event_end" };

            var sort = EisKnowledgeProvider.BuildSort(options, settings);

            Assert.Equal("custom_event_end", sort.First().Field!.Field!.Name);
        }
    }
}
