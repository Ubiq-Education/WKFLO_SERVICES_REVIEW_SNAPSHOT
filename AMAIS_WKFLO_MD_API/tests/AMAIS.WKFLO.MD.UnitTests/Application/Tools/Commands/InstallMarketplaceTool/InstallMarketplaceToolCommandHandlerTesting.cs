using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.InstallMarketplaceTool
{
    public class InstallMarketplaceToolCommandHandlerTesting
    {
        public InstallMarketplaceToolCommandHandler InstallMarketplaceToolCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int MarketplaceToolId { get; set; } = 1;
        public int OAuthMarketplaceToolId { get; set; } = 2;
        public int NonOAuthMcpWithCredentialsMarketplaceToolId { get; set; } = 3;
        public int NonOAuthMcpMarketplaceToolId { get; set; } = 4;
        public const string NonOAuthMcpConfig = "{\"transportType\":\"Http\",\"endpoint\":\"https://mcp.example.com/mcp\",\"authType\":\"None\"}";
        public int TenantId { get; set; } = 1;
        public string TenantName { get; set; } = "TestTenant";

        public InstallMarketplaceToolCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = TenantName };

            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tenant = new Tenant()
            {
                Id = TenantId,
                Name = TenantName,
                Region = "UK"
            };

            var marketplaceTool = new MarketplaceTool()
            {
                Id = MarketplaceToolId,
                Name = "Test Tool",
                Description = "Test Tool Description",
                Type = "API",
                Config = "{\"endpoint\": \"https://api.example.com\"}",
                CreatedOn = fixedDate,
                IsDeleted = false
            };

            var oauthMcpConfig = new McpServerConfig
            {
                Id = "humanticmcp",
                Name = "Humantic",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.humantic.ai/mcp",
                AuthType = McpAuthType.OAuth2,
                Scopes = new[] { "profiles:read" },
                ClientId = "leaked-client-id",
                ClientSecret = "leaked-client-secret",
                RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback"
            };

            var oauthMarketplaceTool = new MarketplaceTool()
            {
                Id = OAuthMarketplaceToolId,
                Name = "Humantic MCP",
                Description = "Humantic MCP tool",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(oauthMcpConfig),
                CreatedOn = fixedDate,
                IsDeleted = false
            };

            var strayMarketplaceTool = new MarketplaceTool()
            {
                Id = NonOAuthMcpWithCredentialsMarketplaceToolId,
                Name = "Plain MCP",
                Description = "MCP without OAuth, stray credentials",
                Type = CommonToolTypeConsts.MCP,
                Config = "{\"id\":\"s\",\"name\":\"S\",\"transportType\":\"Http\",\"endpoint\":\"https://mcp.example.com/mcp\",\"authType\":\"None\",\"ClientId\":\"stray-client\",\"ClientSecret\":\"stray-secret\"}",
                CreatedOn = fixedDate,
                IsDeleted = false
            };

            var plainMcpMarketplaceTool = new MarketplaceTool()
            {
                Id = NonOAuthMcpMarketplaceToolId,
                Name = "Plain MCP 2",
                Description = "MCP without OAuth or credentials",
                Type = CommonToolTypeConsts.MCP,
                Config = NonOAuthMcpConfig,
                CreatedOn = fixedDate,
                IsDeleted = false
            };

            var marketplaceTools = new List<MarketplaceTool>() { marketplaceTool, oauthMarketplaceTool, strayMarketplaceTool, plainMcpMarketplaceTool }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();
            var tenantTools = new List<TenantTool>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceTools).Returns(marketplaceTools.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            var mediator = new Mock<IWkfloMediator>();

            InstallMarketplaceToolCommandHandler = new InstallMarketplaceToolCommandHandler(
                db.Object,
                mapper,
                mediator.Object,
                tenantContext
            );
        }
    }
}

