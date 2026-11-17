using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.GalleryFeature;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using ModelContextProtocol.Protocol;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.Amais.GalleryFeature
{
    public class GalleryFeaturePluginListRenditionsTests
    {
        private const string ListGalleryRenditionsToolName = "list_gallery_renditions";

        private static (GalleryFeaturePlugin Plugin, Mock<IMcpClientService> McpClientServiceMock) BuildPlugin()
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
                            Text = "{\"renditions\":[]}",
                            Meta = new System.Text.Json.Nodes.JsonObject()
                        }
                    }
                });

            var plugin = new GalleryFeaturePlugin(
                mcpClientServiceMock.Object,
                Mock.Of<IDateTimeProvider>(),
                new UserContext(),
                new ChatContext(),
                Mock.Of<IBlobStorageProvider>());

            return (plugin, mcpClientServiceMock);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditions_WithSearchTermsOnly_DoesNotSendRenditionIds()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            await plugin.ListAvailableGalleryFeatureRenditions("image");

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                ListGalleryRenditionsToolName,
                It.Is<Dictionary<string, object>>(d =>
                    d.Count == 1 &&
                    (string)d["searchTerms"] == "image"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditions_WithRenditionIds_ForwardsThem()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            await plugin.ListAvailableGalleryFeatureRenditions(renditionIds: [5243, 6815]);

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                ListGalleryRenditionsToolName,
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey("searchTerms") && d["searchTerms"] == null &&
                    d.ContainsKey("renditionIds") && ((List<int>)d["renditionIds"]).SequenceEqual(new[] { 5243, 6815 })),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditions_WithEmptyRenditionIds_DoesNotSendRenditionIds()
        {
            var (plugin, mcpClientServiceMock) = BuildPlugin();

            await plugin.ListAvailableGalleryFeatureRenditions("logo", []);

            mcpClientServiceMock.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                ListGalleryRenditionsToolName,
                It.Is<Dictionary<string, object>>(d => !d.ContainsKey("renditionIds")),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
