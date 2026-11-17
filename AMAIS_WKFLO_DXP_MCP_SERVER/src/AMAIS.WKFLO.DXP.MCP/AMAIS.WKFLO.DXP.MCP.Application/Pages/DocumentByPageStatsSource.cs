using AMAIS.DXP.REPORT.Client.Lib.Api;
using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Queries;
using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Results;
using AMAIS.WKFLO.DXP.MCP.Application.Models;

namespace AMAIS.WKFLO.DXP.MCP.Application.Pages
{
    /// <summary>
    /// Per-page source backed by the Report API <c>document-by-page</c> endpoint, which returns the period's metrics
    /// and the equal-length previous period's in a single call.
    /// </summary>
    /// <remarks>
    /// The page is identified by its CMS page ID when known, otherwise by its canonical URL (the endpoint accepts
    /// exactly one of the two). When the page ID finds no visits in either period (e.g. older Visit events carry no
    /// <c>objectMetadata.pageId</c>), the page's own canonical URL is tried once. There is never a fallback to another
    /// URL, including the site root: reporting another page's figures is worse than <c>found: false</c>.
    /// Report API errors (<c>ApiException</c>) are left to the caller and never trigger the fallback.
    /// </remarks>
    public sealed class DocumentByPageStatsSource : IDxpPageStatsSource
    {
        /// <summary>Tells the model the figures were matched by the page's URL because its CMS page ID had no visits.</summary>
        internal const string UrlFallbackNote =
            "No visits were recorded under this page's CMS page ID, so these figures were matched by the page's URL instead.";

        private readonly IReportApi _reportApi;

        public DocumentByPageStatsSource(IReportApi reportApi)
        {
            _reportApi = reportApi;
        }

        public async Task<DxpPagePerformance> GetPagePerformanceAsync(string tenantName, DxpPageReference page, DxpDateWindow period, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantName);
            ArgumentNullException.ThrowIfNull(page);

            if (page.PageId == null)
            {
                return await GetByUrlAsync(tenantName, page.CanonicalUrl!, period, cancellationToken);
            }

            var byPageId = await QueryAsync(tenantName, new GetDocumentByPageQuery { PageId = page.PageId }, period, cancellationToken);
            if (byPageId.Current != null || byPageId.Previous != null || !CanFallBackToUrl(page.CanonicalUrl))
            {
                return byPageId;
            }

            var byUrl = await GetByUrlAsync(tenantName, page.CanonicalUrl!, period, cancellationToken);
            return byUrl.Current != null || byUrl.Previous != null
                ? byUrl with { Note = UrlFallbackNote }
                : byUrl;
        }

        /// <summary>
        /// Only the page's own URL qualifies: never the site root, which a homepage alias (e.g. "/home") must not borrow.
        /// </summary>
        private static bool CanFallBackToUrl(Uri? canonicalUrl) =>
            canonicalUrl != null && PagePath.NormalizePath(canonicalUrl.AbsolutePath) != PagePath.Homepage;

        private Task<DxpPagePerformance> GetByUrlAsync(string tenantName, Uri canonicalUrl, DxpDateWindow period, CancellationToken cancellationToken) =>
            QueryAsync(tenantName, new GetDocumentByPageQuery { Url = canonicalUrl.AbsoluteUri }, period, cancellationToken);

        private async Task<DxpPagePerformance> QueryAsync(string tenantName, GetDocumentByPageQuery query, DxpDateWindow period, CancellationToken cancellationToken)
        {
            query.DateFrom = period.From;
            query.DateTo = period.To;

            var response = await _reportApi.DocumentByPageAsync(query, tenantName).WaitAsync(cancellationToken);
            var data = response?.Data;

            return new DxpPagePerformance(ToMetrics(data), ToMetrics(data?.PreviousPeriod));
        }

        /// <summary>
        /// The endpoint answers a page without data with zero counts rather than a 404, so no visits means no metrics.
        /// </summary>
        private static PageMetrics? ToMetrics(DocumentPageStatsResult? stats) =>
            stats == null || stats.TotalCount <= 0 ? null : PageMetrics.From(stats);
    }
}
