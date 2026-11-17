using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.PageAnalytics;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using ModelContextProtocol.Protocol;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.Amais.PageAnalytics
{
    public class DxpPagePluginTests
    {
        private static (DxpPagePlugin Plugin, Mock<IMcpClientService> McpClientServiceMock) BuildPlugin(string currentPagePath = null, string currentPageId = null)
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
                        new TextContentBlock { Text = "{\"found\":true}", Meta = new System.Text.Json.Nodes.JsonObject() }
                    }
                });

            var plugin = new DxpPagePlugin(
                mcpClientServiceMock.Object,
                Mock.Of<IDateTimeProvider>(),
                new UserContext { RawToken = "cms-token" },
                new ChatContext { PagePath = currentPagePath, PageId = currentPageId });

            return (plugin, mcpClientServiceMock);
        }

        [Fact]
        public async Task GetPagePerformance_WithoutPagePath_SendsCurrentPageIdAndPath()
        {
            var (plugin, mcp) = BuildPlugin(currentPagePath: "/admissions", currentPageId: "1234");

            var result = await plugin.GetPagePerformance(lastDays: 7);

            Assert.Equal("{\"found\":true}", Assert.Single(result).Message);
            mcp.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                "get_page_performance",
                It.Is<Dictionary<string, object>>(d =>
                    (string)d["pageId"] == "1234" && (string)d["pagePath"] == "/admissions" && (int)d["lastDays"] == 7),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPagePerformance_WithoutPageId_SendsOnlyPath()
        {
            var (plugin, mcp) = BuildPlugin(currentPagePath: "/admissions");

            await plugin.GetPagePerformance();

            mcp.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                "get_page_performance",
                It.Is<Dictionary<string, object>>(d => (string)d["pagePath"] == "/admissions" && !d.ContainsKey("pageId")),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPagePerformance_WithOtherExplicitPath_DoesNotSendCurrentPageId()
        {
            var (plugin, mcp) = BuildPlugin(currentPagePath: "/admissions", currentPageId: "1234");

            await plugin.GetPagePerformance(" /news ");

            mcp.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                "get_page_performance",
                It.Is<Dictionary<string, object>>(d => (string)d["pagePath"] == "/news" && !d.ContainsKey("pageId")),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPagePerformance_WithNoPageAtAll_DoesNotCallMcp()
        {
            var (plugin, mcp) = BuildPlugin();

            var result = await plugin.GetPagePerformance();

            Assert.Contains("current page is unknown", Assert.Single(result).Message);
            mcp.Verify(m => m.CallToolAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetTopPages_ForwardsArguments()
        {
            var (plugin, mcp) = BuildPlugin();

            await plugin.GetTopPages(lastDays: 14, top: 5);

            mcp.Verify(m => m.CallToolAsync(
                It.IsAny<string>(),
                "get_top_pages",
                It.Is<Dictionary<string, object>>(d => (int)d["lastDays"] == 14 && (int)d["top"] == 5 && !d.ContainsKey("tenant")),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Constructor_RegistersDxpServerConfigWithForwardedBearerToken()
        {
            var mcp = new Mock<IMcpClientService>();

            _ = new DxpPagePlugin(mcp.Object, Mock.Of<IDateTimeProvider>(), new UserContext { RawToken = "cms-token" }, new ChatContext());

            mcp.Verify(m => m.AddServerConfig(It.Is<AMAIS.WKFLO.Common.Models.Config.McpServerConfig>(c =>
                c.Id == "AMAIS DXP MCP Page Analytics plugin"
                && c.AdditionalHeaders["Authorization"] == "Bearer cms-token")), Times.Once);
        }

        [Theory]
        [InlineData(null, "/admissions", "1234", "1234", "/admissions")]
        [InlineData("  ", "/admissions", "1234", "1234", "/admissions")]
        [InlineData("Admissions/", "/admissions", "1234", "1234", "/admissions")]
        [InlineData("/news", "/admissions", "1234", null, "/news")]
        [InlineData(null, null, "1234", "1234", null)]
        [InlineData(null, "/admissions", null, null, "/admissions")]
        [InlineData(null, null, null, null, null)]
        [InlineData(null, "  ", "  ", null, null)]
        public void ResolvePage_UsesCurrentPageUnlessAnotherPathIsRequested(
            string requested, string currentPath, string currentId, string expectedId, string expectedPath)
        {
            var (pageId, pagePath) = DxpPagePlugin.ResolvePage(requested, new ChatContext { PagePath = currentPath, PageId = currentId });

            Assert.Equal(expectedId, pageId);
            Assert.Equal(expectedPath, pagePath);
        }
    }
}
