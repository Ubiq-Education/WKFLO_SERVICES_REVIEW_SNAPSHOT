using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.MD.Application.Tools.Commands.ShareTenantTool;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.ShareTenantTool
{
    public class ShareTenantToolCommandHandlerTesting
    {
        private const string TenantName = "Test";
        private static readonly DateTimeOffset FixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public ShareTenantToolCommandHandler Handler { get; }

        /// <summary>TenantTool.Id of an OAuth2 MCP tool with a matching, already-published PublishedTenantTool.</summary>
        public int OAuthTenantToolId { get; } = 1;

        /// <summary>TenantTool.Id of a non-MCP tool with a matching, already-published PublishedTenantTool.</summary>
        public int NonMcpTenantToolId { get; } = 2;

        /// <summary>TenantTool.Id with no corresponding PublishedTenantTool at all.</summary>
        public int NeverPublishedTenantToolId { get; } = 3;

        /// <summary>A non-OAuth2 MCP tool carrying stray (PascalCase) client credentials.</summary>
        public int NonOAuthMcpWithCredentialsTenantToolId { get; } = 4;

        public ShareTenantToolCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };
            var tenant = new Tenant { Id = 1, Name = TenantName };

            var oauthConfig = new McpServerConfig
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

            var oauthTenantTool = new TenantTool
            {
                Id = OAuthTenantToolId,
                Name = "Humantic MCP",
                Description = "Humantic MCP tool",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(oauthConfig),
                Tenant = tenant,
                IsActive = true,
                CreatedOn = FixedDate
            };

            var oauthPublishedTool = new PublishedTenantTool
            {
                Id = 101,
                Name = oauthTenantTool.Name,
                Description = oauthTenantTool.Description,
                Type = oauthTenantTool.Type,
                Config = oauthTenantTool.Config,
                Tenant = tenant,
                IsActive = true,
                CreatedByTenantToolId = OAuthTenantToolId,
                CreatedOn = FixedDate
            };

            var nonMcpConfig = "{\"endpoint\":\"https://api.example.com\"}";

            var nonMcpTenantTool = new TenantTool
            {
                Id = NonMcpTenantToolId,
                Name = "Plain HTTP tool",
                Description = "Not MCP",
                Type = CommonToolTypeConsts.Http,
                Config = nonMcpConfig,
                Tenant = tenant,
                IsActive = true,
                CreatedOn = FixedDate
            };

            var nonMcpPublishedTool = new PublishedTenantTool
            {
                Id = 102,
                Name = nonMcpTenantTool.Name,
                Description = nonMcpTenantTool.Description,
                Type = nonMcpTenantTool.Type,
                Config = nonMcpTenantTool.Config,
                Tenant = tenant,
                IsActive = true,
                CreatedByTenantToolId = NonMcpTenantToolId,
                CreatedOn = FixedDate
            };

            var neverPublishedTenantTool = new TenantTool
            {
                Id = NeverPublishedTenantToolId,
                Name = "Never published tool",
                Description = "No published copy",
                Type = CommonToolTypeConsts.Http,
                Config = "{}",
                Tenant = tenant,
                IsActive = true,
                CreatedOn = FixedDate
            };

            var strayTenantTool = new TenantTool
            {
                Id = NonOAuthMcpWithCredentialsTenantToolId,
                Name = "Plain MCP",
                Description = "MCP without OAuth",
                Type = CommonToolTypeConsts.MCP,
                Config = "{\"id\":\"s\",\"name\":\"S\",\"transportType\":\"Http\",\"endpoint\":\"https://mcp.example.com/mcp\",\"authType\":\"None\",\"ClientId\":\"stray-client\",\"ClientSecret\":\"stray-secret\"}",
                Tenant = tenant,
                IsActive = true,
                CreatedOn = FixedDate
            };

            var strayPublishedTool = new PublishedTenantTool
            {
                Id = 104,
                Name = strayTenantTool.Name,
                Description = strayTenantTool.Description,
                Type = strayTenantTool.Type,
                Config = strayTenantTool.Config,
                Tenant = tenant,
                IsActive = true,
                CreatedByTenantToolId = NonOAuthMcpWithCredentialsTenantToolId,
                CreatedOn = FixedDate
            };

            var tenantTools = new List<TenantTool> { oauthTenantTool, nonMcpTenantTool, neverPublishedTenantTool, strayTenantTool }.BuildMockDbSet();
            var publishedTools = new List<PublishedTenantTool> { oauthPublishedTool, nonMcpPublishedTool, strayPublishedTool }.BuildMockDbSet();
            var marketplaceTools = new List<MarketplaceTool>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.PublishedTenantTools).Returns(publishedTools.Object);
            db.Setup(x => x.MarketplaceTools).Returns(marketplaceTools.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            var mapper = WkfloMapperExtensions.CreateMapper();

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(FixedDate);

            Handler = new ShareTenantToolCommandHandler(db.Object, mapper, tenantContext, dateTimeProviderMock.Object);
        }
    }
}
