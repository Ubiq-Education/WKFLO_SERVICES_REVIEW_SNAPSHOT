using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.PageAnalytics
{
    /// <summary>
    /// DXP page analytics for MAISY, served by the DXP MCP server (<c>AMAIS_DXP_MCP_SERVER</c>). The caller's CMS
    /// token is forwarded as-is; the DXP MCP derives the site/tenant from it, never from these arguments.
    /// </summary>
    /// <remarks>
    /// Loaded on demand by the skill selector (catalog entry <see cref="AmaisPluginCatalog.PageAnalyticsName"/>),
    /// only when this deployment has <c>AMAIS_DXP_MCP_SERVER</c> set and PetraCMS sent the
    /// <see cref="AmaisPluginCatalog.DxpPageAnalyticsCode"/> capability. Nothing here is auto-invoked.
    /// </remarks>
    public sealed class DxpPagePlugin : BaseAmaisPlugin
    {
        public const string McpServerEnvironmentVariable = "AMAIS_DXP_MCP_SERVER";

        internal const string GetPagePerformanceToolName = "get_page_performance";
        internal const string GetTopPagesToolName = "get_top_pages";

        private const string McpServerId = "AMAIS DXP MCP Page Analytics plugin";

        public DxpPagePlugin(IMcpClientService mcpService, IDateTimeProvider dateTimeProvider, UserContext userContext, ChatContext chatContext)
            : base(McpServerId, Environment.GetEnvironmentVariable(McpServerEnvironmentVariable), mcpService, dateTimeProvider, userContext, chatContext)
        {
        }

        public static bool IsMcpServerConfigured() =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(McpServerEnvironmentVariable));

        [KernelFunction(GetPagePerformanceToolName)]
        [Description("Get DXP engagement metrics for one page of the site (views, unique visitors, engagement time, bounce, recirculation, dead-page and conversion rates) for a period, plus the previous period of the same length for trends. " +
            "Use it when the user asks how a page is performing or how to improve it. Leave pagePath empty to use the page the user is currently on.")]
        public async Task<List<PluginResultItem>> GetPagePerformance(
            [Description("Path of the page, e.g. \"/admissions\" (\"/\" is the homepage). Leave empty for the current page.")] string pagePath = null,
            [Description("Number of days to look back, including today (1-365). Defaults to 30.")] int lastDays = 30)
        {
            var (resolvedPageId, resolvedPath) = ResolvePage(pagePath, _chatContext);
            if (resolvedPageId == null && resolvedPath == null)
            {
                return
                [
                    new PluginResultItem
                    {
                        Message = "No page was specified and the current page is unknown. Ask the user which page to analyse.",
                        Metadata = new Dictionary<string, object> { { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() } }
                    }
                ];
            }

            return await RunAsync(
                async () => await _mcpClientService.CallToolAsync(_mcpServerId, GetPagePerformanceToolName, BuildPagePerformanceArguments(resolvedPageId, resolvedPath, lastDays), CancellationToken.None),
                GenerateMetadataMessageEmpty,
                "There was an error retrieving the page performance data.");
        }

        [KernelFunction(GetTopPagesToolName)]
        [Description("List the site's most viewed pages for a period with their DXP engagement metrics. " +
            "Use it to compare a page with the rest of the site or to find the best and worst performing pages.")]
        public async Task<List<PluginResultItem>> GetTopPages(
            [Description("Number of days to look back, including today (1-365). Defaults to 30.")] int lastDays = 30,
            [Description("Maximum number of pages to return (1-50). Defaults to 10.")] int top = 10)
        {
            return await RunAsync(
                async () => await _mcpClientService.CallToolAsync(_mcpServerId, GetTopPagesToolName, new Dictionary<string, object>
                {
                    { "lastDays", lastDays },
                    { "top", top }
                }, CancellationToken.None),
                GenerateMetadataMessageEmpty,
                "There was an error retrieving the top pages data.");
        }

        /// <summary>
        /// The page to analyse. With no argument (or the current page's own path) it is the page the user is on:
        /// its CMS ID (<see cref="ChatContext.PageId"/>) plus its path. Another explicit path is sent on its own, since
        /// the current page's ID does not identify it. Both values are <c>null</c> when nothing identifies a page.
        /// </summary>
        internal static (string PageId, string PagePath) ResolvePage(string requestedPagePath, ChatContext chatContext)
        {
            var currentPageId = string.IsNullOrWhiteSpace(chatContext?.PageId) ? null : chatContext.PageId.Trim();
            var currentPath = string.IsNullOrWhiteSpace(chatContext?.PagePath) ? null : chatContext.PagePath.Trim();

            if (string.IsNullOrWhiteSpace(requestedPagePath))
                return (currentPageId, currentPath);

            var requested = requestedPagePath.Trim();
            return IsSamePath(requested, currentPath) ? (currentPageId, currentPath) : (null, requested);
        }

        internal static Dictionary<string, object> BuildPagePerformanceArguments(string pageId, string pagePath, int lastDays)
        {
            var arguments = new Dictionary<string, object> { { "lastDays", lastDays } };
            if (pageId != null)
                arguments["pageId"] = pageId;
            if (pagePath != null)
                arguments["pagePath"] = pagePath;
            return arguments;
        }

        private static bool IsSamePath(string a, string b) =>
            b != null && string.Equals(a.Trim('/'), b.Trim('/'), StringComparison.OrdinalIgnoreCase);
    }
}
