using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DisconnectToolMcpOAuth;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.DisconnectToolMcpOAuth
{
    public class DisconnectToolMcpOAuthCommandHandlerTesting
    {
        private const string TenantName = "Test";

        public DisconnectToolMcpOAuthCommandHandler Handler { get; }
        public Mock<IMcpOAuthTokenStore> TokenStoreMock { get; } = new();
        public int ToolId { get; } = 1;
        public int PerUserToolId { get; } = 2;
        public Guid McpOAuthConnectionId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public Guid PerUserMcpOAuthConnectionId { get; } = Guid.Parse("33333333-4444-4444-4444-333333333333");

        /// <summary>The authenticated caller for every request in this fixture.</summary>
        public Guid CallingUserId { get; } = Guid.Parse("dddddddd-2222-2222-2222-222222222222");

        public DisconnectToolMcpOAuthCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };
            var userContext = new UserContext { Guid = CallingUserId.ToString() };
            var tenant = new Tenant { Id = 1, Name = TenantName };

            var tool = new TenantTool
            {
                Id = ToolId,
                Name = "Humantic",
                Description = "Humantic MCP",
                Type = "MCP",
                Config = "{}",
                McpOAuthConnectionId = McpOAuthConnectionId,
                Tenant = tenant,
                IsActive = true
            };

            var perUserConfig = new McpServerConfig
            {
                Id = "canva-placeholder",
                Name = "Canva",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.canva.com/mcp",
                AuthType = McpAuthType.OAuth2,
                OAuthScope = McpOAuthScope.PerUser
            };
            var perUserTool = new TenantTool
            {
                Id = PerUserToolId,
                Name = "Canva",
                Description = "Canva MCP",
                Type = "MCP",
                Config = JsonConvert.SerializeObject(perUserConfig),
                McpOAuthConnectionId = PerUserMcpOAuthConnectionId,
                Tenant = tenant,
                IsActive = true
            };

            var tenantTools = new List<TenantTool> { tool, perUserTool }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);

            TokenStoreMock
                .Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Handler = new DisconnectToolMcpOAuthCommandHandler(db.Object, tenantContext, userContext, TokenStoreMock.Object);
        }
    }
}
