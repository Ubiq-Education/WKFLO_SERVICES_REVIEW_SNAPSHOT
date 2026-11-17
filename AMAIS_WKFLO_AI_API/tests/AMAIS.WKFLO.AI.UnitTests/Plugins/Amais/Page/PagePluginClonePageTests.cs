using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Page;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using ModelContextProtocol.Protocol;
using Microsoft.SemanticKernel;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.Amais.Page
{
    public class PagePluginClonePageTests
    {
        private const string ClonePageToolName = "clone_page";

        private static (PagePlugin Plugin, Mock<IMcpClientService> McpClientServiceMock) BuildPlugin()
        {
            var mcpClientServiceMock = new Mock<IMcpClientService>();
            mcpClientServiceMock
                .Setup(m => m.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            mcpClientServiceMock
                .Setup(m => m.CallToolAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallToolResult
                {
                    IsError = false,
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock
                        {
                            Text = "Page cloned.",
                            Meta = new System.Text.Json.Nodes.JsonObject()
                        }
                    }
                });

            var plugin = new PagePlugin(
                mcpClientServiceMock.Object,
                Mock.Of<IDateTimeProvider>(),
                new UserContext(),
                new ChatContext(),
                Mock.Of<IBlobStorageProvider>());

            return (plugin, mcpClientServiceMock);
        }

        [Fact]
        public async Task ClonePage_WhenBothOptionsOmitted_DoesNotCallMcpToolAndAsksForBothOptions()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            var result = await plugin.ClonePage("page-alias", "New Page Name");

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            var message = Assert.Single(result).Message;
            Assert.Contains("clearSeoAndSocialMetadata", message);
            Assert.Contains("versionUnsharedFeatures", message);
        }

        [Fact]
        public async Task ClonePage_WhenOnlyClearSeoAndSocialMetadataProvided_DoesNotCallMcpToolAndAsksForVersionUnsharedFeatures()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            var result = await plugin.ClonePage("page-alias", "New Page Name", clearSeoAndSocialMetadata: true);

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            var message = Assert.Single(result).Message;
            Assert.Contains("versionUnsharedFeatures", message);
            Assert.DoesNotContain("clearSeoAndSocialMetadata (", message);
        }

        [Fact]
        public async Task ClonePage_WhenOnlyVersionUnsharedFeaturesProvided_DoesNotCallMcpToolAndAsksForClearSeoAndSocialMetadata()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            var result = await plugin.ClonePage("page-alias", "New Page Name", versionUnsharedFeatures: true);

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            var message = Assert.Single(result).Message;
            Assert.Contains("clearSeoAndSocialMetadata", message);
            Assert.DoesNotContain("versionUnsharedFeatures (", message);
        }

        [Fact]
        public async Task ClonePage_WhenBothOptionsExplicitlyFalse_SendsFalseToMcpTool()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            await plugin.ClonePage("page-alias", "New Page Name", clearSeoAndSocialMetadata: false, versionUnsharedFeatures: false);

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                ClonePageToolName,
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey("pageAliasToClone") && (string)d["pageAliasToClone"] == "page-alias" &&
                    d.ContainsKey("newPageName") && (string)d["newPageName"] == "New Page Name" &&
                    d.ContainsKey("clearSeoAndSocialMetadata") && (bool)d["clearSeoAndSocialMetadata"] == false &&
                    d.ContainsKey("versionUnsharedFeatures") && (bool)d["versionUnsharedFeatures"] == false),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ClonePage_WhenClearSeoAndSocialMetadataTrue_SendsTrueToMcpTool()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            await plugin.ClonePage("page-alias", "New Page Name", clearSeoAndSocialMetadata: true, versionUnsharedFeatures: false);

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                ClonePageToolName,
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey("clearSeoAndSocialMetadata") && (bool)d["clearSeoAndSocialMetadata"] == true &&
                    d.ContainsKey("versionUnsharedFeatures") && (bool)d["versionUnsharedFeatures"] == false),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ClonePage_WhenVersionUnsharedFeaturesTrue_SendsTrueToMcpTool()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            await plugin.ClonePage("page-alias", "New Page Name", clearSeoAndSocialMetadata: false, versionUnsharedFeatures: true);

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                ClonePageToolName,
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey("clearSeoAndSocialMetadata") && (bool)d["clearSeoAndSocialMetadata"] == false &&
                    d.ContainsKey("versionUnsharedFeatures") && (bool)d["versionUnsharedFeatures"] == true),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ClonePage_WhenOneOptionFalseAndOtherOmitted_TreatsFalseAsAnswerAndAsksOnlyForTheOther()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            var result = await plugin.ClonePage("page-alias", "New Page Name", clearSeoAndSocialMetadata: false);

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            var message = Assert.Single(result).Message;
            Assert.Contains("versionUnsharedFeatures", message);
            Assert.DoesNotContain("clearSeoAndSocialMetadata (", message);
        }

        [Fact]
        public async Task ClonePage_WhenInvokedThroughKernelWithOptionsOmitted_OptionsAreOptionalAndMcpToolIsNotCalled()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();
            var kernelPlugin = KernelPluginFactory.CreateFromObject(plugin);
            var function = kernelPlugin["clone_page"];

            Assert.False(function.Metadata.Parameters.Single(p => p.Name == "clearSeoAndSocialMetadata").IsRequired);
            Assert.False(function.Metadata.Parameters.Single(p => p.Name == "versionUnsharedFeatures").IsRequired);

            var result = await function.InvokeAsync(new Kernel(), new KernelArguments
            {
                ["pageAliasToClone"] = "page-alias",
                ["newPageName"] = "New Page Name"
            });

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            var items = result.GetValue<List<PluginResultItem>>();
            Assert.NotNull(items);
            var message = Assert.Single(items).Message;
            Assert.Contains("clearSeoAndSocialMetadata", message);
            Assert.Contains("versionUnsharedFeatures", message);
        }
    }
}
