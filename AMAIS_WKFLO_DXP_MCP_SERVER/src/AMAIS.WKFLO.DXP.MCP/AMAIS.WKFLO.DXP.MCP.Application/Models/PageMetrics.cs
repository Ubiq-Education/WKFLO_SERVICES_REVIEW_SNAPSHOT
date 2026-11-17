using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Results;
using AMAIS.WKFLO.DXP.MCP.Application.Pages;

namespace AMAIS.WKFLO.DXP.MCP.Application.Models
{
    /// <summary>
    /// Compact projection of a DXP document row, kept small because it is sent to the model.
    /// Rates are percentages (0-100) rounded to one decimal place.
    /// </summary>
    public sealed record PageMetrics(
        string Path,
        string Url,
        long Views,
        int UniqueVisitors,
        long EngagementSecs,
        double? AvgEngagementSecs,
        decimal? BounceRate,
        decimal? RecirculationRate,
        decimal? DeadPageRate,
        decimal? ConversionRate,
        DateTimeOffset? LastVisit)
    {
        /// <summary>A row of the Report API <c>/documents</c> list.</summary>
        public static PageMetrics From(AggregationDocumentResponseResult document)
        {
            ArgumentNullException.ThrowIfNull(document);

            return Create(document.Object, document.TotalCount, document.UniqueCount, document.EngagementTimeInSecs,
                document.BounceRate, document.RecirculationRate, document.DeadPageRate, document.ConversionRate, document.LastUpdate);
        }

        /// <summary>One period of the Report API <c>document-by-page</c> result.</summary>
        public static PageMetrics From(DocumentPageStatsResult stats)
        {
            ArgumentNullException.ThrowIfNull(stats);

            return Create(stats.Url, stats.TotalCount, stats.UniqueCount, stats.EngagementTimeInSecs,
                stats.BounceRate, stats.RecirculationRate, stats.DeadPageRate, stats.ConversionRate, stats.LastUpdate);
        }

        private static PageMetrics Create(
            string? url,
            long views,
            int uniqueVisitors,
            double engagementTimeInSecs,
            decimal? bounceRate,
            decimal? recirculationRate,
            decimal? deadPageRate,
            decimal? conversionRate,
            DateTimeOffset? lastUpdate) => new(
                Path: PagePath.FromDocumentUrl(url) ?? url ?? string.Empty,
                Url: url ?? string.Empty,
                Views: views,
                UniqueVisitors: uniqueVisitors,
                EngagementSecs: (long)Math.Round(engagementTimeInSecs),
                AvgEngagementSecs: views > 0 ? Math.Round(engagementTimeInSecs / views, 1) : null,
                BounceRate: Round(bounceRate),
                RecirculationRate: Round(recirculationRate),
                DeadPageRate: Round(deadPageRate),
                ConversionRate: Round(conversionRate),
                LastVisit: lastUpdate == null || lastUpdate == default(DateTimeOffset) ? null : lastUpdate);

        private static decimal? Round(decimal? value) => value.HasValue ? Math.Round(value.Value, 1) : null;
    }
}
