using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.SkillSelector;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.Amais.SkillSelector
{
    public class AmaisSkillSelectorPluginTests
    {
        private const string PluginNamePrefix = "plugin_1_Integration_Amais";

        private static readonly IReadOnlyList<AmaisPluginCatalog.Entry> AllCatalogEntries =
            AmaisPluginCatalog.GetOfferable(null, ["Welcome", "Post", "Page", "GalleryFeature", "TextFeature"]);

        private static Kernel BuildKernel(string classificationRawResponse = null, System.Exception classificationException = null)
        {
            var builder = Kernel.CreateBuilder();

            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            if (classificationException != null)
            {
                chatCompletionServiceMock.Setup(x => x.GetChatMessageContentsAsync(
                        It.IsAny<ChatHistory>(),
                        It.IsAny<PromptExecutionSettings>(),
                        It.IsAny<Kernel>(),
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(classificationException);
            }
            else
            {
                IReadOnlyList<ChatMessageContent> response = [new ChatMessageContent(AuthorRole.Assistant, classificationRawResponse)];
                chatCompletionServiceMock.Setup(x => x.GetChatMessageContentsAsync(
                        It.IsAny<ChatHistory>(),
                        It.IsAny<PromptExecutionSettings>(),
                        It.IsAny<Kernel>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(response);
            }

            builder.Services.AddSingleton(chatCompletionServiceMock.Object);

            // Dependencies required to actually construct the AMAIS plugin types via ActivatorUtilities
            // when AmaisSkillSelectorPlugin.SelectPluginsAsync calls kernel.Plugins.AddFromType<T>.
            builder.Services.AddSingleton(Mock.Of<IMcpClientService>());
            builder.Services.AddSingleton(Mock.Of<IDateTimeProvider>());
            builder.Services.AddSingleton(new UserContext());
            builder.Services.AddSingleton(new ChatContext());
            builder.Services.AddSingleton(Mock.Of<IBlobStorageProvider>());

            return builder.Build();
        }

        private static IEnumerable<string> LoadedPluginNames(Kernel kernel) =>
            kernel.Plugins.Select(p => p.Name.Replace(PluginNamePrefix + "_", string.Empty));

        private static IEnumerable<string> LoadedFunctionNames(Kernel kernel, string pluginShortName) =>
            kernel.Plugins.Single(p => p.Name == PluginNamePrefix + "_" + pluginShortName).Select(f => f.Name);

        // ── Constructor takes a pre-filtered list ────────────────────────────

        [Fact]
        public async Task SelectPluginsAsync_ClassificationReturnsSubsetOfAvailable_LoadsOnlyThatSubset()
        {
            var kernel = BuildKernel(classificationRawResponse: "[\"Welcome\", \"Post\"]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), AllCatalogEntries, null);

            await plugin.SelectPluginsAsync("user chat history", "I need to publish a post");

            Assert.Equal(["Post", "Welcome"], LoadedPluginNames(kernel).OrderBy(n => n).ToArray());
        }

        [Fact]
        public async Task SelectPluginsAsync_ClassificationReturnsNameNotInPreFilteredList_DoesNotLoadIt()
        {
            // Simulates a fully-gated-out skill (e.g. TextFeature, none of whose endpoints are
            // available) being excluded from the constructor's pre-filtered list, even though the
            // LLM's raw response still names it — the caller (SemanticKernelExtensions) is the only
            // place allowed to decide offerability.
            var restrictedList = AmaisPluginCatalog.GetOfferable(null, ["Welcome", "Post"]);
            var kernel = BuildKernel(classificationRawResponse: "[\"Welcome\", \"TextFeature\"]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), restrictedList, null);

            await plugin.SelectPluginsAsync("user chat history", "some request");

            var loaded = LoadedPluginNames(kernel).ToArray();
            Assert.Contains("Welcome", loaded);
            Assert.DoesNotContain("TextFeature", loaded);
        }

        [Fact]
        public async Task SelectPluginsAsync_NoPluginsSelected_ReturnsAlreadyLoadedMessage()
        {
            var kernel = BuildKernel(classificationRawResponse: "[]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), AllCatalogEntries, null);

            var result = await plugin.SelectPluginsAsync("history", "generic small talk");

            Assert.Equal("All required plugins are already loaded. You can proceed.", result);
            Assert.Empty(kernel.Plugins);
        }

        [Fact]
        public async Task SelectPluginsAsync_AlreadyLoadedPlugin_SkipsReloadingIt()
        {
            var kernel = BuildKernel(classificationRawResponse: "[\"Welcome\"]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), AllCatalogEntries, null);

            // First call loads Welcome
            await plugin.SelectPluginsAsync("history", "welcome message please");
            Assert.Single(kernel.Plugins);

            // Second call should recognize it's already loaded and not throw / duplicate
            var secondResult = await plugin.SelectPluginsAsync("history", "welcome message please");

            Assert.Single(kernel.Plugins);
            Assert.Equal("All required plugins are already loaded. You can proceed.", secondResult);
        }

        // ── Classification-failure fallback uses the filtered set, not the old static list ──

        [Fact]
        public async Task SelectPluginsAsync_ClassificationThrows_FallsBackToOnlyThePreFilteredPlugins()
        {
            // Constructed with a restricted (gated) list excluding Page/GalleryFeature/TextFeature —
            // a transient LLM failure must not leak those gated-out skills back in via the fallback.
            var restrictedList = AmaisPluginCatalog.GetOfferable(null, ["Welcome", "Post"]);
            var kernel = BuildKernel(classificationException: new System.InvalidOperationException("LLM unavailable"));
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), restrictedList, null);

            var result = await plugin.SelectPluginsAsync("history", "do something");

            var loaded = LoadedPluginNames(kernel).OrderBy(n => n).ToArray();
            Assert.Equal(["Post", "Welcome"], loaded);
            Assert.DoesNotContain("Page", loaded);
            Assert.DoesNotContain("GalleryFeature", loaded);
            Assert.DoesNotContain("TextFeature", loaded);
            Assert.Contains("Welcome", result);
            Assert.Contains("Post", result);
        }

        [Fact]
        public async Task SelectPluginsAsync_ClassificationThrowsWithFullCatalog_FallsBackToAllAvailablePlugins()
        {
            var kernel = BuildKernel(classificationException: new System.InvalidOperationException("LLM unavailable"));
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), AllCatalogEntries, null);

            await plugin.SelectPluginsAsync("history", "do something");

            var loaded = LoadedPluginNames(kernel).OrderBy(n => n).ToArray();
            Assert.Equal(["GalleryFeature", "Page", "Post", "TextFeature", "Welcome"], loaded);
        }

        // ── Function-level filtering within a loaded plugin ─────────────────

        [Fact]
        public async Task SelectPluginsAsync_NullAvailableEndpoints_LoadsEveryFunctionInThePlugin()
        {
            // Fail-open: an AMAIS build predating this mechanism sends no header at all.
            var kernel = BuildKernel(classificationRawResponse: "[\"Post\"]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), AllCatalogEntries, null);

            await plugin.SelectPluginsAsync("history", "publish a post");

            Assert.Equal(
                ["clone_post", "create_post", "list_all_post_images", "update_post", "update_post_image", "update_post_image_by_id"],
                LoadedFunctionNames(kernel, "Post").OrderBy(n => n).ToArray());
        }

        [Fact]
        public async Task SelectPluginsAsync_AvailableEndpointsExcludesSomeFunctions_OnlyLoadsMatchingFunctions()
        {
            // Only the CreatePost ("CRP") and ClonePost ("CLP") endpoint codes are deployed on this
            // PetraCMS build — the other Post functions must not be exposed to the LLM, while these
            // two remain available.
            var kernel = BuildKernel(classificationRawResponse: "[\"Post\"]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), AllCatalogEntries, ["CRP", "CLP"]);

            await plugin.SelectPluginsAsync("history", "publish a post");

            Assert.Equal(["clone_post", "create_post"], LoadedFunctionNames(kernel, "Post").OrderBy(n => n).ToArray());
        }

        [Fact]
        public async Task SelectPluginsAsync_AvailableEndpointsMatchNoFunctions_LoadsPluginWithZeroFunctions()
        {
            var kernel = BuildKernel(classificationRawResponse: "[\"Post\"]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), AllCatalogEntries, ["WLC"]);

            await plugin.SelectPluginsAsync("history", "publish a post");

            Assert.Empty(LoadedFunctionNames(kernel, "Post"));
        }

        [Fact]
        public async Task SelectPluginsAsync_PageAnalyticsWithCapability_LoadsBothDxpFunctions()
        {
            var entries = AmaisPluginCatalog.GetOfferable(["DxpPageAnalytics"], [AmaisPluginCatalog.PageAnalyticsName]);
            var kernel = BuildKernel(classificationRawResponse: "[\"PageAnalytics\"]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), entries, ["DxpPageAnalytics"]);

            await plugin.SelectPluginsAsync("history", "how is this page performing?");

            Assert.Equal(["get_page_performance", "get_top_pages"], LoadedFunctionNames(kernel, AmaisPluginCatalog.PageAnalyticsName).OrderBy(n => n).ToArray());
        }

        [Fact]
        public async Task SelectPluginsAsync_PageAnalyticsWithoutHeader_ExposesNoDxpFunctions()
        {
            // Even if the entry were offered by mistake, fail-closed filtering strips every DXP function when the
            // capability header is missing.
            var entries = AmaisPluginCatalog.All.Where(e => e.Name == AmaisPluginCatalog.PageAnalyticsName).ToList();
            var kernel = BuildKernel(classificationRawResponse: "[\"PageAnalytics\"]");
            var plugin = new AmaisSkillSelectorPlugin(kernel, PluginNamePrefix, Mock.Of<ILogger>(), entries, null);

            await plugin.SelectPluginsAsync("history", "how is this page performing?");

            Assert.Empty(LoadedFunctionNames(kernel, AmaisPluginCatalog.PageAnalyticsName));
        }
    }
}
