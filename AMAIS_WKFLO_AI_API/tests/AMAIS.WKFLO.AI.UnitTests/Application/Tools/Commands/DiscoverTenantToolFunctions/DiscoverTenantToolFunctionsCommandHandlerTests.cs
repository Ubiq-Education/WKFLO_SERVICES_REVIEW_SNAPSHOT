using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.AI.Application.Tools.Commands.DiscoverTenantToolFunctions;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using ModelContextProtocol.Client;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Tools.Commands.DiscoverTenantToolFunctions
{
    /// <summary>WI 7447: refreshing an MCP tool's function snapshot from its server.</summary>
    public class DiscoverTenantToolFunctionsCommandHandlerTests
    {
        private const string TenantName = "TestTenant";
        private const int McpToolId = 7;
        private const int HttpToolId = 8;
        private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

        private readonly Mock<IMcpClientService> _mcp = new();
        private readonly TenantTool _mcpTool;
        private readonly PublishedTenantTool _publishedCopy;
        private readonly Mock<IMasterDataDbContext> _db = new();
        private readonly DiscoverTenantToolFunctionsCommandHandler _handler;

        public DiscoverTenantToolFunctionsCommandHandlerTests()
        {
            var tenant = new Tenant { Id = 1, Name = TenantName };
            _mcpTool = new TenantTool
            {
                Id = McpToolId,
                Name = "Docs",
                Type = "MCP",
                Tenant = tenant,
                Config = JsonConvert.SerializeObject(new McpServerConfig { Id = "docs", Name = "Docs", TransportType = TransportType.Http, Endpoint = "https://mcp.example.com/mcp" }),
                // "search" was already known and reviewed; "old" disappeared from the server.
                DiscoveredFunctions = ToolPermissionJson.SerializeDiscoveredFunctions([new McpFunctionDescriptor { Name = "search", IsNew = false }, new McpFunctionDescriptor { Name = "old" }]),
                EnabledFunctions = ToolPermissionJson.SerializeEnabledFunctions(["search", "old"])
            };
            _publishedCopy = new PublishedTenantTool { Id = 70, Type = "MCP", CreatedByTenantToolId = McpToolId };
            var httpTool = new TenantTool { Id = HttpToolId, Name = "Weather", Type = "HTTP", Tenant = tenant, Config = "{}" };

            _db.Setup(x => x.TenantTools).Returns(new List<TenantTool> { _mcpTool, httpTool }.BuildMockDbSet().Object);
            _db.Setup(x => x.PublishedTenantTools).Returns(new List<PublishedTenantTool> { _publishedCopy }.BuildMockDbSet().Object);
            _db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            _handler = new DiscoverTenantToolFunctionsCommandHandler(_db.Object, _mcp.Object, new TenantContext { TenantName = TenantName },
                new UserContext(), Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), NullLogger<DiscoverTenantToolFunctionsCommandHandler>.Instance);
        }

        private static McpClientTool ClientTool(string name, string description) =>
            new(Mock.Of<McpClient>(), new ModelContextProtocol.Protocol.Tool
            {
                Name = name,
                Description = description,
                InputSchema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"q\":{\"type\":\"string\"}}}").RootElement
            });

        [Fact]
        public async Task Handle_SavesMergedSnapshotOnTenantToolOnly_AndReturnsAllowlistView()
        {
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _mcp.Setup(x => x.ListToolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<McpClientTool> { ClientTool("search", "Search documents"), ClientTool("create", "Create a document") });

            var result = await _handler.Handle(new DiscoverTenantToolFunctionsCommand { Id = McpToolId }, TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<ToolFunctionsResult>>(result).Data;
            Assert.Equal(new[] { "search", "create", "old" }, data.Functions.Select(f => f.Name));
            Assert.Equal(new[] { ToolFunctionStatus.Active, ToolFunctionStatus.New, ToolFunctionStatus.Removed }, data.Functions.Select(f => f.Status));
            Assert.Equal("Search documents", data.Functions[0].Description);
            Assert.Equal("object", data.Functions[0].InputSchema["type"]!.ToString());
            Assert.Equal(Now, data.FunctionsDiscoveredOn);

            Assert.Equal(Now, _mcpTool.FunctionsDiscoveredOn);
            // Draft/publish model: the published copy only changes on publish.
            Assert.Null(_publishedCopy.DiscoveredFunctions);
            Assert.Null(_publishedCopy.FunctionsDiscoveredOn);
            _mcp.Verify(x => x.DisconnectAsync(It.Is<string>(id => id.StartsWith("discover-7-"))), Times.Once);
            _db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_Rediscovery_KeepsDisplayValuesOfFunctionsStillOffered_AndDropsThoseOfRemovedOnes()
        {
            // WI 7726: friendly values are admin data keyed by name; the server's own values always refresh.
            _mcpTool.DiscoveredFunctions = ToolPermissionJson.SerializeDiscoveredFunctions(
            [
                new McpFunctionDescriptor { Name = "search", Description = "Old server text", DisplayName = "Search content", DisplayDescription = "Finds content", IsNew = false },
                new McpFunctionDescriptor { Name = "old", DisplayName = "Old friendly name" }
            ]);
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _mcp.Setup(x => x.ListToolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<McpClientTool> { ClientTool("search", "Search documents"), ClientTool("create", "Create a document") });

            var result = await _handler.Handle(new DiscoverTenantToolFunctionsCommand { Id = McpToolId }, TestContext.Current.CancellationToken);

            var data = Assert.IsType<SuccessResult<ToolFunctionsResult>>(result).Data;
            var search = data.Functions.Single(f => f.Name == "search");
            Assert.Equal("Search documents", search.Description);
            Assert.Equal("Search content", search.DisplayName);
            Assert.Equal("Finds content", search.DisplayDescription);
            Assert.Null(data.Functions.Single(f => f.Name == "create").DisplayName);
            Assert.Null(data.Functions.Single(f => f.Name == "old").DisplayName);

            var stored = ToolPermissionJson.ParseDiscoveredFunctions(_mcpTool.DiscoveredFunctions);
            Assert.Equal(new[] { "search", "create" }, stored.Select(f => f.Name));
            Assert.Equal("Search content", stored[0].DisplayName);
            Assert.Equal("Finds content", stored[0].DisplayDescription);
        }

        [Fact]
        public async Task Handle_ConsentRequired_ReturnsNotConnected_AndDoesNotSave()
        {
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new McpOAuthInteractiveConsentRequiredException("consent needed"));

            var result = await _handler.Handle(new DiscoverTenantToolFunctionsCommand { Id = McpToolId }, TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Equal(ErrorConst.ToolMcpOAuthNotConnected, Assert.Single(invalid.Errors).Code);
            _db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            _mcp.Verify(x => x.DisconnectAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ServerUnreachable_ReturnsUnavailable()
        {
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await _handler.Handle(new DiscoverTenantToolFunctionsCommand { Id = McpToolId }, TestContext.Current.CancellationToken);

            Assert.Equal(ErrorConst.ToolMcpUnavailable, Assert.Single(Assert.IsType<InvalidResult>(result).Errors).Code);
        }

        [Fact]
        public async Task Handle_OAuthToolNeverConnected_ReturnsNotConnectedWithoutConnecting()
        {
            _mcpTool.Config = JsonConvert.SerializeObject(new McpServerConfig
            {
                Id = "docs",
                Name = "Docs",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.example.com/mcp",
                AuthType = McpAuthType.OAuth2,
                Scopes = ["read"]
            });
            _mcpTool.McpOAuthConnectionId = null;

            var result = await _handler.Handle(new DiscoverTenantToolFunctionsCommand { Id = McpToolId }, TestContext.Current.CancellationToken);

            Assert.Equal(ErrorConst.ToolMcpOAuthNotConnected, Assert.Single(Assert.IsType<InvalidResult>(result).Errors).Code);
            _mcp.Verify(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(HttpToolId, ResultType.Invalid)]
        [InlineData(999, ResultType.NotFound)]
        public async Task Handle_NotAnMcpToolOrUnknown_IsRejected(int toolId, ResultType expected)
        {
            var result = await _handler.Handle(new DiscoverTenantToolFunctionsCommand { Id = toolId }, TestContext.Current.CancellationToken);

            Assert.Equal(expected, result.ResultType);
        }
    }
}
