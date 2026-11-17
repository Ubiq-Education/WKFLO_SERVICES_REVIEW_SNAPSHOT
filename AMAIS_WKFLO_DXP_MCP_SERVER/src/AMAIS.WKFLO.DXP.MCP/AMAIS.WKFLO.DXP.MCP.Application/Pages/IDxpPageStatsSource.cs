using AMAIS.WKFLO.DXP.MCP.Application.Models;

namespace AMAIS.WKFLO.DXP.MCP.Application.Pages
{
    /// <summary>
    /// Per-page DXP metrics for a period and the period before it.
    /// </summary>
    /// <remarks>
    /// Implemented by <see cref="DocumentByPageStatsSource"/> (Report API <c>document-by-page</c>); kept behind an
    /// interface so the tools can be tested without the Report API client.
    /// </remarks>
    public interface IDxpPageStatsSource
    {
        /// <param name="tenantName">DXP tenant, always taken from the caller's token.</param>
        /// <param name="page">The page to look up: CMS page ID, canonical URL, or both. At least one is set.</param>
        Task<DxpPagePerformance> GetPagePerformanceAsync(string tenantName, DxpPageReference page, DxpDateWindow period, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Identifies a page by CMS page ID (preferred) and/or canonical URL. At least one must be set.
    /// </summary>
    public sealed record DxpPageReference
    {
        public DxpPageReference(string? pageId, Uri? canonicalUrl)
        {
            PageId = string.IsNullOrWhiteSpace(pageId) ? null : pageId.Trim();
            CanonicalUrl = canonicalUrl;

            if (PageId == null && CanonicalUrl == null)
            {
                throw new ArgumentException("A page ID or a canonical URL is required.");
            }
        }

        public string? PageId { get; }

        public Uri? CanonicalUrl { get; }
    }

    /// <param name="Note">Optional explanation for the model, e.g. that the figures were matched by the page's URL because its page ID had no visits.</param>
    public sealed record DxpPagePerformance(PageMetrics? Current, PageMetrics? Previous, string? Note = null);
}
