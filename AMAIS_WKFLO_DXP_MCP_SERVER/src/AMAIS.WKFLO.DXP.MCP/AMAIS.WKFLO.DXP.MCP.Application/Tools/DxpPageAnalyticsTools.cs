using AMAIS.DXP.Common.Client;
using AMAIS.DXP.REPORT.Client.Lib.Api;
using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Queries;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.DXP.MCP.Application.Callers;
using AMAIS.WKFLO.DXP.MCP.Application.Models;
using AMAIS.WKFLO.DXP.MCP.Application.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CommonMetadataConsts = AMAIS.WKFLO.Common.Models.CommonConsts.CommonMetadataConsts;

namespace AMAIS.WKFLO.DXP.MCP.Application.Tools
{
    /// <summary>
    /// Read-only DXP page engagement tools for MAISY. The DXP tenant always comes from the caller's token
    /// (<see cref="DxpCallerContext"/>), never from tool arguments.
    /// </summary>
    [McpServerToolType]
    [Authorize]
    public sealed class DxpPageAnalyticsTools
    {
        public const string GetTopPagesToolName = "get_top_pages";
        public const string GetPagePerformanceToolName = "get_page_performance";

        public const int DefaultLastDays = 30;
        public const int MaxLastDays = 365;
        public const int DefaultTop = 10;
        public const int MaxTop = 50;

        internal const string ReportApiError = "The DXP analytics service could not be reached. Please try again later.";
        internal const string PageRequiredError = "Specify the page with pageId or pagePath (use \"/\" for the homepage).";
        internal const string NotFoundNote = "No tracked visits for this page in the period or the previous period.";
        internal const string NoCurrentVisitsNote = "No tracked visits for this page in the period; the previous period had visits.";

        private const string MetricGlossary =
            " Metrics (rates are percentages 0-100): " +
            "views = page views in the period; uniqueVisitors = distinct visitors; " +
            "engagementSecs = total active time visitors spent on the page, avgEngagementSecs = active time per view; " +
            "bounceRate = share of visits where this was the only page the visitor viewed that day (they left without going further), " +
            "high values suggest the page does not match what visitors expected or lacks a clear next step - improve the opening content, calls to action and internal links; " +
            "recirculationRate = share of views after which the visitor went on to another page of the site, higher is better - " +
            "raise it with related links, next-step buttons and clear navigation; " +
            "deadPageRate = 100 - bounceRate - recirculationRate, visitors who browsed other pages but whose journey ended on this one, " +
            "high values mean the page is a dead end - add onward links or a call to action; " +
            "conversionRate = share of conversions attributed to this page. " +
            "Bounce, recirculation and dead-page rates are computed over at most the last 15 days of the period.";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IReportApi _reportApi;
        private readonly IDxpPageStatsSource _pageStatsSource;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<DxpPageAnalyticsTools> _logger;

        public DxpPageAnalyticsTools(
            IReportApi reportApi,
            IDxpPageStatsSource pageStatsSource,
            IHttpContextAccessor httpContextAccessor,
            IDateTimeProvider dateTimeProvider,
            ILogger<DxpPageAnalyticsTools> logger)
        {
            _reportApi = reportApi;
            _pageStatsSource = pageStatsSource;
            _httpContextAccessor = httpContextAccessor;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        [McpServerTool(Name = GetTopPagesToolName, Title = "Top pages", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists the most viewed pages of the current site, sorted by views, with their engagement metrics for the period. " +
            "Use it to compare a page with the rest of the site or to find the best and worst performing pages." + MetricGlossary)]
        public async Task<TextContentBlock> GetTopPages(
            [Description("Number of days to look back, including today (1-365). Defaults to 30.")] int lastDays = DefaultLastDays,
            [Description("Maximum number of pages to return (1-50). Defaults to 10.")] int top = DefaultTop,
            CancellationToken cancellationToken = default)
        {
            if (!DxpCallerContext.TryResolve(_httpContextAccessor.HttpContext?.User, out var caller, out var callerError))
            {
                return Error(callerError);
            }

            var period = DxpDateWindow.ForLastDays(_dateTimeProvider.DateTimeUtcNow(), ClampLastDays(lastDays));

            try
            {
                var query = new ListDocumentQuery { DateFrom = period.From, DateTo = period.To };
                var result = await _reportApi.DocumentsListAsync(query, caller.TenantName).WaitAsync(cancellationToken);

                var pages = DxpDocuments.TopPages(result?.Data, ClampTop(top))
                    .Select(PageMetrics.From)
                    .ToList();

                return Success(new TopPagesResult(DxpPeriod.FromWindow(period), pages));
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "DXP Report API returned {StatusCode} listing top pages for tenant {TenantName}", ex.ErrorCode, caller.TenantName);
                return Error(ReportApiError);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error listing DXP top pages for tenant {TenantName}", caller.TenantName);
                return Error(ReportApiError);
            }
        }

        [McpServerTool(Name = GetPagePerformanceToolName, Title = "Page performance", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Gets the engagement metrics of one page of the current site for the period, plus the same metrics for the previous period of equal length so trends can be explained. " +
            "Use it when the user asks how a page (usually the page they are on) is performing or how to improve it. " +
            "Identify the page by pageId (the CMS page ID, preferred) and/or pagePath; at least one is required. " +
            "If found is false the page had no tracked visits in either period. current is null when the period itself had no visits. current.url is the URL the figures belong to." + MetricGlossary)]
        public async Task<TextContentBlock> GetPagePerformance(
            [Description("CMS ID of the page, when known. Preferred over pagePath.")] string? pageId = null,
            [Description("Path of the page, e.g. \"/admissions\". Use \"/\" for the homepage.")] string? pagePath = null,
            [Description("Number of days to look back, including today (1-365). Defaults to 30.")] int lastDays = DefaultLastDays,
            CancellationToken cancellationToken = default)
        {
            if (!DxpCallerContext.TryResolve(_httpContextAccessor.HttpContext?.User, out var caller, out var callerError))
            {
                return Error(callerError);
            }

            var hasPageId = !string.IsNullOrWhiteSpace(pageId);
            if (!hasPageId && string.IsNullOrWhiteSpace(pagePath))
            {
                return Error(PageRequiredError);
            }

            string? normalizedPath = null;
            Uri? canonicalUrl = null;
            if (!string.IsNullOrWhiteSpace(pagePath))
            {
                if (caller.SiteOrigin == null)
                {
                    return Error(DxpCallerContext.MissingSiteOriginError);
                }

                if (!PagePath.TryNormalize(pagePath, out normalizedPath, out var pathError))
                {
                    return Error(pathError);
                }

                canonicalUrl = PagePath.BuildCanonicalUrl(caller.SiteOrigin, normalizedPath);
            }

            var page = new DxpPageReference(pageId, canonicalUrl);
            var period = DxpDateWindow.ForLastDays(_dateTimeProvider.DateTimeUtcNow(), ClampLastDays(lastDays));

            try
            {
                var performance = await _pageStatsSource.GetPagePerformanceAsync(caller.TenantName, page, period, cancellationToken);
                // Found when either period has visits, matching the lookup: only both periods empty means "not found".
                var found = performance.Current != null || performance.Previous != null;
                // The source's note (e.g. figures matched by URL) and the tool's own status note can both apply.
                var statusNote = !found ? NotFoundNote : performance.Current == null ? NoCurrentVisitsNote : null;
                var note = string.Join(" ", new[] { performance.Note, statusNote }.Where(n => !string.IsNullOrEmpty(n)));

                return Success(new PagePerformanceResult(
                    PageId: page.PageId,
                    Path: normalizedPath,
                    Url: canonicalUrl?.AbsoluteUri,
                    Found: found,
                    Period: DxpPeriod.FromWindow(period),
                    Current: performance.Current,
                    PreviousPeriod: DxpPeriod.FromWindow(period.Previous()),
                    Previous: performance.Previous,
                    Note: note.Length == 0 ? null : note));
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "DXP Report API returned {StatusCode} getting page performance for tenant {TenantName}", ex.ErrorCode, caller.TenantName);
                return Error(ReportApiError);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error getting DXP page performance for tenant {TenantName}", caller.TenantName);
                return Error(ReportApiError);
            }
        }

        public static int ClampLastDays(int lastDays) => lastDays < 1 ? DefaultLastDays : Math.Min(lastDays, MaxLastDays);

        public static int ClampTop(int top) => top < 1 ? DefaultTop : Math.Min(top, MaxTop);

        private TextContentBlock Success<T>(T payload) => new()
        {
            Text = JsonSerializer.Serialize(payload, JsonOptions),
            Meta = new JsonObject
            {
                [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow()
            }
        };

        // Same error shape as the AMAIS MCP tools, which the AI API's MCP plugins already understand.
        private TextContentBlock Error(string message) => new()
        {
            Text = $"Error: {message}",
            Meta = new JsonObject
            {
                [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow(),
                [CommonMetadataConsts.IsError] = true
            }
        };
    }
}
