using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.AI.Application.Tools.Commands.StartPublicToolMcpOAuth;
using AMAIS.WKFLO.AI.Application.Tools.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Tools.Commands.StartPublicToolMcpOAuth
{
    public class StartPublicToolMcpOAuthCommandHandlerTests
    {
        private const string ShareId = "share-1";
        private const string OtherAgentShareId = "share-2";

        private static readonly Guid AttachedConnectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid UnattachedConnectionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid CallingUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        private static McpServerConfig BuildOAuthConfig(McpOAuthScope scope = McpOAuthScope.Shared) => new()
        {
            Id = "test-server",
            Name = "placeholder",
            TransportType = TransportType.Http,
            Endpoint = "https://mcp.example.com/mcp",
            AuthType = McpAuthType.OAuth2,
            OAuthScope = scope,
            Scopes = ["read"],
            ClientId = "preconfigured-client",
            ClientSecret = "preconfigured-secret",
            RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback"
        };

        private static (StartPublicToolMcpOAuthCommandHandler Handler, Mock<IMcpOAuthConsentService> ConsentService) BuildHandler(
            McpServerConfig attachedToolConfig, string attachedToolType = CommonToolTypeConsts.MCP)
        {
            var attachedTool = new TenantTool
            {
                Id = 1,
                Name = "Attached Tool",
                Type = attachedToolType,
                Config = JsonConvert.SerializeObject(attachedToolConfig),
                McpOAuthConnectionId = AttachedConnectionId,
                IsActive = true
            };

            var agent = new TenantAgent
            {
                Id = 1,
                Name = "Public Agent",
                AgentTools = new List<AgentTool>
                {
                    new() { AgentId = 1, Agent = null, ToolId = attachedTool.Id, Tool = attachedTool }
                }
            };

            var chatSetting = new AgentChatSetting
            {
                Id = 1,
                ShareId = ShareId,
                AgentId = agent.Id,
                Agent = agent
            };

            // A second agent with no tools at all — used to prove a connection id belonging to some other
            // agent is rejected rather than found by accident.
            var otherAgent = new TenantAgent { Id = 2, Name = "Other Agent", AgentTools = new List<AgentTool>() };
            var otherChatSetting = new AgentChatSetting { Id = 2, ShareId = OtherAgentShareId, AgentId = otherAgent.Id, Agent = otherAgent };

            var chatSettings = new List<AgentChatSetting> { chatSetting, otherChatSetting }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(chatSettings.Object);

            var userContext = new UserContext { Guid = CallingUserId.ToString() };

            var consentService = new Mock<IMcpOAuthConsentService>();
            consentService
                .Setup(x => x.BeginAsync(It.IsAny<McpServerConfig>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Uri("https://mcp.example.com/authorize?state=abc"));

            var handler = new StartPublicToolMcpOAuthCommandHandler(
                db.Object, userContext, consentService.Object, Mock.Of<ILogger<StartPublicToolMcpOAuthCommandHandler>>());

            return (handler, consentService);
        }

        [Fact]
        public async Task Handle_UnknownShareId_ReturnsNotFound()
        {
            var (handler, _) = BuildHandler(BuildOAuthConfig());

            var result = await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = "no-such-share-id", Id = AttachedConnectionId },
                TestContext.Current.CancellationToken);

            var notFound = Assert.IsType<NotFoundResult>(result);
            Assert.Contains(notFound.Errors, e => e.Code == ErrorConst.AgentNotFound);
        }

        [Fact]
        public async Task Handle_ConnectionIdNotAttachedToTheResolvedAgent_ReturnsNotFound_AndNeverCallsBeginAsync()
        {
            var (handler, consentService) = BuildHandler(BuildOAuthConfig());

            // UnattachedConnectionId doesn't belong to any tool on the agent resolved by ShareId — a
            // cross-tenant/cross-agent hijack attempt must never reach BeginAsync at all.
            var result = await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = ShareId, Id = UnattachedConnectionId },
                TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
            consentService.Verify(x => x.BeginAsync(It.IsAny<McpServerConfig>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_ConnectionIdBelongsToADifferentAgent_ReturnsNotFound()
        {
            var (handler, consentService) = BuildHandler(BuildOAuthConfig());

            // AttachedConnectionId is real, but for `share-1`'s agent — not `share-2`'s.
            var result = await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = OtherAgentShareId, Id = AttachedConnectionId },
                TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
            consentService.Verify(x => x.BeginAsync(It.IsAny<McpServerConfig>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_ToolIsNotAnMcpTool_ReturnsInvalid()
        {
            var (handler, _) = BuildHandler(BuildOAuthConfig(), attachedToolType: CommonToolTypeConsts.Http);

            var result = await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = ShareId, Id = AttachedConnectionId },
                TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.ToolMcpOAuthNotConfigured);
        }

        [Fact]
        public async Task Handle_ToolIsNotConfiguredForOAuth2_ReturnsInvalid()
        {
            var config = BuildOAuthConfig();
            config.AuthType = McpAuthType.None;
            var (handler, _) = BuildHandler(config);

            var result = await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = ShareId, Id = AttachedConnectionId },
                TestContext.Current.CancellationToken);

            Assert.IsType<InvalidResult>(result);
        }

        [Fact]
        public async Task Handle_ValidRequest_ResolvesRealUserContextGuidAndReturnsAuthorizationUrl()
        {
            var (handler, consentService) = BuildHandler(BuildOAuthConfig(McpOAuthScope.PerUser));

            var result = await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = ShareId, Id = AttachedConnectionId },
                TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<ToolMcpOAuthStartResult>>(result);
            Assert.Equal("https://mcp.example.com/authorize?state=abc", success.Data.AuthorizationUrl);

            // A PerUser-scope tool must resolve to the request's real, authenticated user id — never null —
            // which is exactly what the AmaisJwtOnly gate on this route guarantees is available.
            consentService.Verify(x => x.BeginAsync(
                It.Is<McpServerConfig>(c => c.Endpoint == "https://mcp.example.com/mcp"),
                AttachedConnectionId,
                CallingUserId,
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_EncryptedClientSecret_IsForwardedAsStoredForBeginAsyncToDecrypt()
        {
            // US 7849: decryption happens at the sink (McpOAuthConsentService.BeginAsync), so the stored
            // "enc:v1:" value must reach it untouched — decrypting here as well would double-decrypt.
            var config = BuildOAuthConfig();
            config.ClientSecret = "enc:v1:ciphertext";
            var (handler, consentService) = BuildHandler(config);

            await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = ShareId, Id = AttachedConnectionId },
                TestContext.Current.CancellationToken);

            consentService.Verify(x => x.BeginAsync(
                It.Is<McpServerConfig>(c => c.ClientSecret == "enc:v1:ciphertext" && c.ClientId == "preconfigured-client"),
                AttachedConnectionId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_SharedScopeTool_ResolvesRequestingUserIdAsNull()
        {
            var (handler, consentService) = BuildHandler(BuildOAuthConfig(McpOAuthScope.Shared));

            await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = ShareId, Id = AttachedConnectionId },
                TestContext.Current.CancellationToken);

            consentService.Verify(x => x.BeginAsync(
                It.IsAny<McpServerConfig>(), AttachedConnectionId, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ConsentServiceThrows_ReturnsInvalidResult()
        {
            var (handler, consentService) = BuildHandler(BuildOAuthConfig());
            consentService
                .Setup(x => x.BeginAsync(It.IsAny<McpServerConfig>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("discovery failed"));

            var result = await handler.Handle(
                new StartPublicToolMcpOAuthCommand { ShareId = ShareId, Id = AttachedConnectionId },
                TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.ToolMcpOAuthConsentFailed);
        }
    }
}
