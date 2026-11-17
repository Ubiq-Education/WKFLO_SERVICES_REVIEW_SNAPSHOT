using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Results;

namespace AMAIS.WKFLO.DXP.MCP.Application.Pages
{
    /// <summary>
    /// Pure helpers over the rows returned by the Report API <c>/documents</c> endpoint.
    /// </summary>
    public static class DxpDocuments
    {
        /// <summary>Row type the Report API uses for web pages (the others are "Document" and "Video").</summary>
        public const string PageType = "Page";

        public static bool IsPage(AggregationDocumentResponseResult? document) =>
            document != null && string.Equals(document.Type, PageType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Pages only, most viewed first (ties broken by unique visitors), capped at <paramref name="top"/>.
        /// </summary>
        public static IReadOnlyList<AggregationDocumentResponseResult> TopPages(IEnumerable<AggregationDocumentResponseResult>? documents, int top)
        {
            if (documents == null || top <= 0)
            {
                return [];
            }

            return documents
                .Where(IsPage)
                .OrderByDescending(d => d.TotalCount)
                .ThenByDescending(d => d.UniqueCount)
                .Take(top)
                .ToList();
        }
    }
}
