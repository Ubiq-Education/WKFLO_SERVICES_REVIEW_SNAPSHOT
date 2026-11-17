using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.GalleryFeature;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Page;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.PageAnalytics;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Post;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.TextFeature;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Welcome;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.SkillSelector
{
    public sealed class AmaisSkillSelectorPlugin
    {
        private readonly Kernel _kernel;
        private readonly string _pluginNamePrefix;
        private readonly ILogger _logger;

        /// <summary>
        /// The plugins this instance is allowed to select from — pre-filtered by the caller
        /// (<c>SemanticKernelExtensions.AddWkflowToolsAsync</c>) against
        /// <see cref="AmaisPluginCatalog"/> for the current request's available AMAIS endpoints. This
        /// class never re-derives offerability itself, so a plugin gated out at construction time can
        /// never be selected, including via the classification-failure fallback below.
        /// </summary>
        private readonly IReadOnlyList<AmaisPluginCatalog.Entry> _availablePlugins;

        /// <summary>
        /// The AMAIS endpoint class names available for this request (see
        /// <see cref="AMAIS.WKFLO.Common.Contexts.ChatContext.AmaisAvailableEndpoints"/>), used to filter
        /// individual KernelFunctions within a selected plugin down to only those whose backing endpoint
        /// is actually deployed. <c>null</c>/empty is fail-open (every function kept).
        /// </summary>
        private readonly IReadOnlyCollection<string> _availableEndpoints;

        public AmaisSkillSelectorPlugin(Kernel kernel, string pluginNamePrefix, ILogger logger, IReadOnlyList<AmaisPluginCatalog.Entry> availablePlugins, IReadOnlyCollection<string> availableEndpoints)
        {
            _kernel = kernel;
            _pluginNamePrefix = pluginNamePrefix;
            _logger = logger;
            _availablePlugins = availablePlugins;
            _availableEndpoints = availableEndpoints;
        }

        public async Task<string> SelectPluginsAsync(string userChatHistory, string userQuery)
        {
            var selected = await ClassifyPluginsAsync(userChatHistory, userQuery);
            var loaded = new List<string>();

            foreach (var name in selected)
            {
                var fullName = _pluginNamePrefix + "_" + name;
                if (_kernel.Plugins.Any(p => p.Name == fullName))
                    continue;

                switch (name)
                {
                    case "Welcome":
                        _kernel.Plugins.Add(AmaisPluginCatalog.CreateFilteredPlugin<WelcomePlugin>(_kernel.Services, fullName, "Welcome", _availableEndpoints));
                        loaded.Add("Welcome");
                        break;
                    case "Post":
                        _kernel.Plugins.Add(AmaisPluginCatalog.CreateFilteredPlugin<PostPlugin>(_kernel.Services, fullName, "Post", _availableEndpoints));
                        loaded.Add("Post");
                        break;
                    case "Page":
                        _kernel.Plugins.Add(AmaisPluginCatalog.CreateFilteredPlugin<PagePlugin>(_kernel.Services, fullName, "Page", _availableEndpoints));
                        loaded.Add("Page");
                        break;
                    case "GalleryFeature":
                        _kernel.Plugins.Add(AmaisPluginCatalog.CreateFilteredPlugin<GalleryFeaturePlugin>(_kernel.Services, fullName, "GalleryFeature", _availableEndpoints));
                        loaded.Add("GalleryFeature");
                        break;
                    case "TextFeature":
                        _kernel.Plugins.Add(AmaisPluginCatalog.CreateFilteredPlugin<TextFeaturePlugin>(_kernel.Services, fullName, "TextFeature", _availableEndpoints));
                        loaded.Add("TextFeature");
                        break;
                    case AmaisPluginCatalog.PageAnalyticsName:
                        _kernel.Plugins.Add(AmaisPluginCatalog.CreateFilteredPlugin<DxpPagePlugin>(_kernel.Services, fullName, AmaisPluginCatalog.PageAnalyticsName, _availableEndpoints));
                        loaded.Add(AmaisPluginCatalog.PageAnalyticsName);
                        break;
                }
            }

            return loaded.Count == 0
                ? "All required plugins are already loaded. You can proceed."
                : $"Loaded AMAIS plugins: {string.Join(", ", loaded)}. You can now call their functions.";
        }

        private async Task<List<string>> ClassifyPluginsAsync(string userChatHistory, string userQuery)
        {
            try
            {
                var pluginDescriptions = string.Join("\n",
                    _availablePlugins.Select(p => $"- {p.Name}: {p.Description}"));

                var prompt = $"""
                    Given the following user request, determine which AMAIS plugins/tools/skills are needed to fulfill it.

                    Available plugins/tools/skills:
                    {pluginDescriptions}

                    User chat history: {userChatHistory}

                    User request: {userQuery}

                    Respond ONLY with a valid JSON array of plugin names. Examples:
                    ["Post"]
                    ["Page", "GalleryFeature"]
                    ["Welcome", "Page", "GalleryFeature"]
                    ["Welcome", "Page", "Post", "GalleryFeature"]
                    ["Welcome", "Page", "Post", "GalleryFeature", "TextFeature"]
                    []

                    Please suggest all tools when the scope of the user query is very generic.
                    """;

                var history = new ChatHistory();
                history.AddSystemMessage("You are a plugin selector. Respond only with a valid JSON array of plugin names from the provided list.");
                history.AddUserMessage(prompt);

                var settings = new PromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.None() };
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var response = await chatCompletion.GetChatMessageContentAsync(history, settings);

                return ParseResponse(response.Content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AmaisSkillSelectorPlugin classification failed; falling back to all plugins");
                return _availablePlugins.Select(p => p.Name).ToList();
            }
        }

        private List<string> ParseResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return [];

            // Strip markdown code fences if present
            var trimmed = content.Trim();
            if (trimmed.StartsWith("```"))
            {
                var lines = trimmed.Split('\n');
                trimmed = string.Join("\n", lines.Skip(1).Take(lines.Length - 2));
            }

            var parsed = JsonConvert.DeserializeObject<List<string>>(trimmed) ?? [];

            var validNames = _availablePlugins.Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return parsed
                .Where(n => validNames.Contains(n))
                .Select(n => validNames.First(v => string.Equals(v, n, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }
}
