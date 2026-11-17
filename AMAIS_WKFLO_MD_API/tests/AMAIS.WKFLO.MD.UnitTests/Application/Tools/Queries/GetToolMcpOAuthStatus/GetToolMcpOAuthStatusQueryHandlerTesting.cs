using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetToolMcpOAuthStatus;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetToolMcpOAuthStatus
{
    public class GetToolMcpOAuthStatusQueryHandlerTesting
    {
        private const string TenantName = "Test";

        public GetToolMcpOAuthStatusQueryHandler Handler { get; }

        /// <summary>Token exists, no connection outcome recorded yet (LastCheckSucceeded == null) — reads as Connected.</summary>
        public int ConnectedToolId { get; } = 1;
        public int NotConnectedToolId { get; } = 2;

        /// <summary>Token exists, last recorded connection attempt failed (LastCheckSucceeded == false).</summary>
        public int ConnectionIssueToolId { get; } = 3;

        /// <summary>Token exists, last recorded connection attempt explicitly succeeded (LastCheckSucceeded == true).</summary>
        public int ConnectedAfterSuccessfulCheckToolId { get; } = 4;

        public DateTimeOffset ConnectedOnUtc { get; } = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public string ConnectionIssueError { get; } = "401 { \"error\": { \"code\": \"AUTH\", \"message\": \"No active subscription for this account.\" } }";

        public Guid ConnectedConnectionId { get; } = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public Guid NotConnectedConnectionId { get; } = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        public Guid ConnectionIssueConnectionId { get; } = Guid.Parse("66666666-6666-6666-6666-666666666666");
        public Guid ConnectedAfterSuccessfulCheckConnectionId { get; } = Guid.Parse("77777777-7777-7777-7777-777777777777");

        /// <summary>A PerUser-scope tool (e.g. Canva) with a token row for a DIFFERENT user than the one
        /// calling — the caller must still read as NotConnected, since per-user status is never "anyone at
        /// the tenant is connected".</summary>
        public int PerUserToolId { get; } = 5;
        public Guid PerUserConnectionId { get; } = Guid.Parse("88888888-9999-9999-9999-888888888888");

        /// <summary>The authenticated caller for every request in this fixture.</summary>
        public Guid CallingUserId { get; } = Guid.Parse("dddddddd-3333-3333-3333-333333333333");
        public Guid OtherUserId { get; } = Guid.Parse("dddddddd-4444-4444-4444-444444444444");

        public GetToolMcpOAuthStatusQueryHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };
            var userContext = new UserContext { Guid = CallingUserId.ToString() };
            var tenant = new Tenant { Id = 1, Name = TenantName };

            var connectedTool = new TenantTool
            {
                Id = ConnectedToolId,
                Name = "Humantic",
                Description = "Humantic MCP",
                Type = "MCP",
                Config = "{}",
                McpOAuthConnectionId = ConnectedConnectionId,
                Tenant = tenant,
                IsActive = true
            };

            // Provisioned (has a connection id, per eager provisioning) but never actually connected — no
            // ToolOAuthToken row exists for it.
            var notConnectedTool = new TenantTool
            {
                Id = NotConnectedToolId,
                Name = "Humantic (not connected)",
                Description = "Humantic MCP",
                Type = "MCP",
                Config = "{}",
                McpOAuthConnectionId = NotConnectedConnectionId,
                Tenant = tenant,
                IsActive = true
            };

            var connectionIssueTool = new TenantTool
            {
                Id = ConnectionIssueToolId,
                Name = "Humantic (connection issue)",
                Description = "Humantic MCP",
                Type = "MCP",
                Config = "{}",
                McpOAuthConnectionId = ConnectionIssueConnectionId,
                Tenant = tenant,
                IsActive = true
            };

            var connectedAfterSuccessfulCheckTool = new TenantTool
            {
                Id = ConnectedAfterSuccessfulCheckToolId,
                Name = "Humantic (checked ok)",
                Description = "Humantic MCP",
                Type = "MCP",
                Config = "{}",
                McpOAuthConnectionId = ConnectedAfterSuccessfulCheckConnectionId,
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
                McpOAuthConnectionId = PerUserConnectionId,
                Tenant = tenant,
                IsActive = true
            };

            var tenantTools = new List<TenantTool>
            {
                connectedTool, notConnectedTool, connectionIssueTool, connectedAfterSuccessfulCheckTool, perUserTool
            }.BuildMockDbSet();

            var token = new ToolOAuthToken
            {
                Id = 1,
                McpOAuthConnectionId = ConnectedConnectionId,
                AccessToken = "encrypted-access-token",
                ConnectedOnUtc = ConnectedOnUtc,
                TokenObtainedAtUtc = ConnectedOnUtc,
                LastCheckSucceeded = null
            };

            var connectionIssueToken = new ToolOAuthToken
            {
                Id = 2,
                McpOAuthConnectionId = ConnectionIssueConnectionId,
                AccessToken = "encrypted-access-token",
                ConnectedOnUtc = ConnectedOnUtc,
                TokenObtainedAtUtc = ConnectedOnUtc,
                LastCheckedOnUtc = ConnectedOnUtc,
                LastCheckSucceeded = false,
                LastError = ConnectionIssueError
            };

            var connectedAfterSuccessfulCheckToken = new ToolOAuthToken
            {
                Id = 3,
                McpOAuthConnectionId = ConnectedAfterSuccessfulCheckConnectionId,
                AccessToken = "encrypted-access-token",
                ConnectedOnUtc = ConnectedOnUtc,
                TokenObtainedAtUtc = ConnectedOnUtc,
                LastCheckedOnUtc = ConnectedOnUtc,
                LastCheckSucceeded = true
            };

            // Belongs to OtherUserId, not CallingUserId — proves per-user status is scoped to the caller.
            var perUserTokenForOtherUser = new ToolOAuthToken
            {
                Id = 4,
                McpOAuthConnectionId = PerUserConnectionId,
                UserId = OtherUserId,
                AccessToken = "encrypted-access-token",
                ConnectedOnUtc = ConnectedOnUtc,
                TokenObtainedAtUtc = ConnectedOnUtc
            };

            var tokens = new List<ToolOAuthToken>
            {
                token, connectionIssueToken, connectedAfterSuccessfulCheckToken, perUserTokenForOtherUser
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.ToolOAuthTokens).Returns(tokens.Object);

            Handler = new GetToolMcpOAuthStatusQueryHandler(db.Object, tenantContext, userContext);
        }
    }
}
