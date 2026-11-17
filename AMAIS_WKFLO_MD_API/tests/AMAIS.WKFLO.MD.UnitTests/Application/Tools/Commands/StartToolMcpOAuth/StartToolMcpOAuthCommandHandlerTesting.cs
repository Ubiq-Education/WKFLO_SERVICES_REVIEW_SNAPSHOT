using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.MD.Application.Tools.Commands.StartToolMcpOAuth;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.StartToolMcpOAuth
{
    public class StartToolMcpOAuthCommandHandlerTesting
    {
        private const string TenantName = "Test";

        public StartToolMcpOAuthCommandHandler Handler { get; }
        public Mock<IMcpOAuthConsentService> ConsentServiceMock { get; } = new();

        public int OAuthToolId { get; }
        public int NonOAuthToolId { get; }
        public int NonMcpToolId { get; }
        public int PerUserOAuthToolId { get; }

        // Every OAuth2 MCP tool is provisioned with its McpOAuthConnectionId as soon as it's saved (see
        // McpOAuthConnectionIdProvisioner, called from Create/UpdateTenantToolCommandHandler) — so by the
        // time Start is ever reachable, the id already exists. These fixtures reflect that: every tool below
        // already carries one, matching real post-save state.
        public Guid OAuthConnectionId { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Guid NonOAuthConnectionId { get; } = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public Guid NonMcpConnectionId { get; } = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        public Guid PerUserOAuthConnectionId { get; } = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        /// <summary>The authenticated caller for every request in this fixture (UserContext.Guid) — resolved
        /// as the requesting user for a PerUser-scope tool (e.g. Canva), ignored for Shared-scope tools (e.g.
        /// Humantic).</summary>
        public Guid CallingUserId { get; } = Guid.Parse("dddddddd-1111-1111-1111-111111111111");

        /// <summary>The tracked OAuth2 tool entity.</summary>
        public TenantTool OAuthTool { get; }

        public StartToolMcpOAuthCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };
            var userContext = new UserContext { Guid = CallingUserId.ToString() };
            var tenant = new Tenant { Id = 1, Name = TenantName };

            var oauthConfig = new McpServerConfig
            {
                Id = "placeholder",
                Name = "Humantic",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.humantic.ai/mcp",
                AuthType = McpAuthType.OAuth2,
                Scopes = new[] { "profiles:read" }
            };

            var oauthTool = new TenantTool
            {
                Id = 1,
                Name = "Humantic",
                Description = "Humantic MCP",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(oauthConfig),
                McpOAuthConnectionId = OAuthConnectionId,
                Tenant = tenant,
                IsActive = true
            };
            OAuthToolId = oauthTool.Id;
            OAuthTool = oauthTool;

            var nonOAuthConfig = new McpServerConfig
            {
                Id = "placeholder",
                Name = "Static",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.example.com/mcp",
                AuthType = McpAuthType.None
            };

            // Simulates a tool that was previously OAuth2 (hence has a connection id) but got reconfigured
            // to a non-OAuth2 auth type — Start must still reject it cleanly.
            var nonOAuthTool = new TenantTool
            {
                Id = 2,
                Name = "Static",
                Description = "Static MCP",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(nonOAuthConfig),
                McpOAuthConnectionId = NonOAuthConnectionId,
                Tenant = tenant,
                IsActive = true
            };
            NonOAuthToolId = nonOAuthTool.Id;

            var nonMcpTool = new TenantTool
            {
                Id = 3,
                Name = "Some HTTP tool",
                Description = "Not MCP",
                Type = CommonToolTypeConsts.Http,
                Config = "{}",
                McpOAuthConnectionId = NonMcpConnectionId,
                Tenant = tenant,
                IsActive = true
            };
            NonMcpToolId = nonMcpTool.Id;

            var perUserOAuthConfig = new McpServerConfig
            {
                Id = "canva-placeholder",
                Name = "Canva",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.canva.com/mcp",
                AuthType = McpAuthType.OAuth2,
                OAuthScope = McpOAuthScope.PerUser,
                Scopes = new[] { "design:content:read" }
            };

            // Canva has no tenant/org-shared mode at all — every tenant tool that connects to it must be
            // configured OAuthScope = PerUser.
            var perUserOAuthTool = new TenantTool
            {
                Id = 4,
                Name = "Canva",
                Description = "Canva MCP",
                Type = CommonToolTypeConsts.MCP,
                Config = JsonConvert.SerializeObject(perUserOAuthConfig),
                McpOAuthConnectionId = PerUserOAuthConnectionId,
                Tenant = tenant,
                IsActive = true
            };
            PerUserOAuthToolId = perUserOAuthTool.Id;

            var tenantTools = new List<TenantTool> { oauthTool, nonOAuthTool, nonMcpTool, perUserOAuthTool }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);

            ConsentServiceMock
                .Setup(x => x.BeginAsync(It.IsAny<McpServerConfig>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Uri("https://mcp.humantic.ai/authorize?code_challenge=abc"));

            var settings = Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://wkflo.example.com" });

            Handler = new StartToolMcpOAuthCommandHandler(
                db.Object,
                tenantContext,
                userContext,
                ConsentServiceMock.Object,
                settings,
                Mock.Of<ILogger<StartToolMcpOAuthCommandHandler>>());
        }
    }
}
