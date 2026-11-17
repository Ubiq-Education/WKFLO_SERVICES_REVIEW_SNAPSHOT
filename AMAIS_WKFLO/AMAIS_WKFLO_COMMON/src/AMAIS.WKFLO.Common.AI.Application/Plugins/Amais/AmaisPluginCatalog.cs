using Microsoft.SemanticKernel;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais
{
    /// <summary>
    /// Single shared catalog of every AMAIS plugin/skill reachable through the header-based
    /// AMAIS-endpoints gate (<see cref="AMAIS.WKFLO.Common.Models.CommonConsts.AmaisAvailableEndpoints"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Knowledge.AmaisKnowledgePlugin"/> is not
    /// part of <c>AmaisSkillSelectorPlugin.AvailablePlugins</c> today — it's registered unconditionally
    /// alongside the skill selector in <c>SemanticKernelExtensions.AddWkflowToolsAsync</c> — so the
    /// catalog can't just live inside that plugin; it needs to cover both call sites.
    /// <para>
    /// Availability is no longer a build-version comparison (different AMAIS environments have
    /// unrelated build numbers, so that never worked reliably). Instead, PetraCMS reports a static,
    /// hand-maintained list of short opaque codes for the endpoints it exposes to AMAIS/WKFlo
    /// (<c>BaseController.GetAvailableAmaisEndpoints()</c> in PetraCMS — deliberately not a literal
    /// endpoint class name, so nothing descriptive of PetraCMS's internal API surface goes out over
    /// the wire), and each plugin KernelFunction here is mapped 1:1 to the one code representing the
    /// PetraCMS endpoint it calls through the MCP server. A function is available only if its mapped
    /// code is present in that list.
    /// </para>
    /// <para>
    /// The codes below are an independent, hand-kept-in-sync copy of PetraCMS's list — a mismatch
    /// there silently gates out (or leaves ungated) the affected function rather than throwing, so
    /// keep both sides updated together. See PLAN.md in the task worktree for the canonical
    /// endpoint-to-code table.
    /// </para>
    /// <para>
    /// Fail-open: a <c>null</c>/empty available-endpoints list means the caller's AMAIS build predates
    /// this mechanism (doesn't send the header at all), so every function is treated as available —
    /// identical to pre-ticket behavior. Once a non-empty list is present, matching is exact.
    /// </para>
    /// </remarks>
    public static class AmaisPluginCatalog
    {
        /// <param name="EndpointsByToolName">
        /// Maps each KernelFunction tool name (the string passed to <c>[KernelFunction(...)]</c>) to the
        /// short opaque code PetraCMS uses for the endpoint that backs it (see
        /// <c>BaseController.GetAvailableAmaisEndpoints()</c> in PetraCMS). <c>null</c> means this
        /// entry isn't backed by any PetraCMS API endpoint (e.g. <c>Knowledge</c>, which is
        /// Elasticsearch-backed) — never gated.
        /// </param>
        /// <param name="RequiresCapability">
        /// <c>true</c> for entries that are fail-closed: offered only when one of their codes is explicitly
        /// present in the available-endpoints header, even when the header is missing or empty. Used for
        /// capabilities a PetraCMS instance only has when extra configuration exists (e.g. DXP), where the
        /// legacy fail-open rule would offer tools the site cannot back.
        /// </param>
        public sealed record Entry(string Name, string Description, IReadOnlyDictionary<string, string> EndpointsByToolName = null, bool RequiresCapability = false);

        /// <summary>Catalog name of the DXP page analytics skill (<c>DxpPagePlugin</c>).</summary>
        public const string PageAnalyticsName = "PageAnalytics";

        /// <summary>
        /// Capability code PetraCMS sends in the available-endpoints header only when DXP is configured
        /// for the site (Task 7751). Gates every DXP page analytics function.
        /// </summary>
        public const string DxpPageAnalyticsCode = "DxpPageAnalytics";

        private static readonly string[] BaseSkillSelectorNames = ["Welcome", "Post", "Page", "GalleryFeature", "TextFeature"];

        /// <summary>
        /// Candidate names for the skill selector when AMAIS skills are enabled. <c>PageAnalytics</c> is only a
        /// candidate when this deployment has a DXP MCP server configured (<c>AMAIS_DXP_MCP_SERVER</c>); whether a
        /// given request may use it is still decided per request by <see cref="GetOfferable"/>.
        /// </summary>
        public static IReadOnlyList<string> GetSkillSelectorCandidateNames(bool isDxpMcpServerConfigured) =>
            isDxpMcpServerConfigured ? [.. BaseSkillSelectorNames, PageAnalyticsName] : BaseSkillSelectorNames;

        public static readonly IReadOnlyList<Entry> All =
        [
            new("Knowledge", "Retrieves relevant school knowledge base passages to answer the user's question."),
            new("Welcome", "When user ask for welcome message.", new Dictionary<string, string>
            {
                ["get_welcome_message"] = "WLC", // Home.WelcomeMessage
            }),
            new("Post", "Creates, clones, updates blog posts and manages post images.", new Dictionary<string, string>
            {
                ["create_post"] = "CRP", // Post.CreatePost
                ["clone_post"] = "CLP", // Post.ClonePost
                ["update_post"] = "UPP", // Post.UpdatePost
                ["update_post_image"] = "UPI", // Post.UpdatePostImageByImage
                ["update_post_image_by_id"] = "UPR", // Post.UpdatePostImageByRenditionId
                ["list_all_post_images"] = "LPI", // Post.ListAvailablePostImages
            }),
            new("Page", "Creates, clones, updates pages, retrieves page data, manages page images.", new Dictionary<string, string>
            {
                ["create_page"] = "CRG", // Page.CreatePage
                ["clone_page"] = "CLG", // Page.ClonePage
                ["update_page"] = "UPG", // Page.UpdatePage
                ["update_page_image"] = "UGI", // Page.UpdatePageImageByImage
                ["update_page_image_by_id"] = "UGR", // Page.UpdatePageImageByRenditionId
                ["get_page_data"] = "GPA", // Page.GetPageByAlias
                ["list_all_page_images"] = "LGI", // Page.ListAvailablePageImages
                ["get_page_templates"] = "LTP", // Page.ListTemplatesByPopularity
            }),
            new("GalleryFeature", "Creates, updates, lists, and retrieves a specific gallery feature by ID, including its image renditions.", new Dictionary<string, string>
            {
                ["create_gallery_feature"] = "CGF", // Features.GalleryFeature.CreateGalleryFeature
                ["update_gallery_feature"] = "UGF", // Features.GalleryFeature.UpdateGalleryFeature
                ["list_gallery_renditions"] = "LGR", // Features.GalleryFeature.ListAvailableGalleryFeatureRenditions
                ["list_gallery_features"] = "LGF", // Features.GalleryFeature.ListGalleryFeatures
                ["get_gallery_feature"] = "GGF", // Features.GalleryFeature.GetGalleryFeature
            }),
            new("TextFeature", "Retrieves a specific feature's saved and published text content by feature ID.", new Dictionary<string, string>
            {
                ["get_text_feature"] = "GTF", // Features.TextFeature.GetTextFeature
            }),
            new(PageAnalyticsName, "Page performance analytics from DXP (views, visitors, engagement time, bounce, recirculation and dead-page rates) for the current page or the site's most visited pages, to explain how a page performs and suggest improvements.", new Dictionary<string, string>
            {
                ["get_page_performance"] = DxpPageAnalyticsCode, // DXP MCP: per-page metrics
                ["get_top_pages"] = DxpPageAnalyticsCode, // DXP MCP: most viewed pages
            }, RequiresCapability: true),
        ];

        /// <summary>
        /// A missing/empty available-endpoints list means the caller didn't send the header at all —
        /// an AMAIS build predating this mechanism. Treated as fail-open (nothing is gated).
        /// </summary>
        public static bool IsFailOpen(IReadOnlyCollection<string> availableEndpoints) =>
            availableEndpoints == null || availableEndpoints.Count == 0;

        /// <summary>
        /// Whether the named catalog entry should be offered to the skill-selector classifier at all:
        /// fail-open, not endpoint-backed (e.g. <c>Knowledge</c>), or at least one of its functions'
        /// backing endpoints is present. Returns <c>false</c> when no entry matches <paramref name="name"/>.
        /// </summary>
        public static bool IsOfferable(string name, IReadOnlyCollection<string> availableEndpoints)
        {
            var entry = All.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return false;

            if (entry.EndpointsByToolName == null)
                return true;

            if (IsFailOpen(availableEndpoints))
                return !entry.RequiresCapability;

            var availableSet = new HashSet<string>(availableEndpoints, StringComparer.OrdinalIgnoreCase);
            return entry.EndpointsByToolName.Values.Any(availableSet.Contains);
        }

        /// <summary>
        /// Filters <paramref name="candidateNames"/> down to the catalog entries offerable for
        /// <paramref name="availableEndpoints"/>, preserving catalog order. Names not present in the
        /// catalog are silently dropped.
        /// </summary>
        public static IReadOnlyList<Entry> GetOfferable(IReadOnlyCollection<string> availableEndpoints, IEnumerable<string> candidateNames)
        {
            var candidateSet = new HashSet<string>(candidateNames ?? [], StringComparer.OrdinalIgnoreCase);

            return All
                .Where(e => candidateSet.Contains(e.Name) && IsOfferable(e.Name, availableEndpoints))
                .ToList();
        }

        /// <summary>
        /// Builds the <typeparamref name="T"/> plugin via the usual DI-backed <see cref="KernelPluginFactory.CreateFromType{T}"/>,
        /// then — unless fail-open — drops any KernelFunction whose mapped endpoint (per the named
        /// catalog entry) isn't in <paramref name="availableEndpoints"/>. Sibling functions of the same
        /// plugin whose endpoints ARE available are kept, so a partially-deployed skill still exposes
        /// its available functions instead of being all-or-nothing.
        /// </summary>
        public static KernelPlugin CreateFilteredPlugin<T>(IServiceProvider services, string pluginName, string catalogName, IReadOnlyCollection<string> availableEndpoints)
        {
            var plugin = KernelPluginFactory.CreateFromType<T>(pluginName, services);

            var entry = All.FirstOrDefault(e => string.Equals(e.Name, catalogName, StringComparison.OrdinalIgnoreCase));
            if (entry?.EndpointsByToolName == null || (IsFailOpen(availableEndpoints) && !entry.RequiresCapability))
                return plugin;

            var availableSet = new HashSet<string>(availableEndpoints ?? [], StringComparer.OrdinalIgnoreCase);
            var filtered = plugin.Where(f =>
                !entry.EndpointsByToolName.TryGetValue(f.Name, out var endpoint) || availableSet.Contains(endpoint));

            return KernelPluginFactory.CreateFromFunctions(pluginName, filtered);
        }
    }
}
