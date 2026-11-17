using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Knowledge;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Moq;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Extensions
{
    /// <summary>
    /// Covers Task 6972: for an Integration_Amais tool, AmaisKnowledgePlugin must always be
    /// registered (regardless of AmaisIntegrationToolConfiguration), while AddWkflowToolsAsync's
    /// return value — which callers use to gate AutoSelectAmaisPluginsAsync/AutoInvokePageDataAsync/
    /// AutoInvokeGalleryFeatureDataAsync/AutoInvokeTextFeatureDataAsync — must reflect
    /// AmaisIntegrationToolConfiguration.AreSkillsEnabled exactly.
    /// </summary>
    public class SemanticKernelExtensionsAddWkflowToolsAsyncTests
    {
        private static Kernel BuildKernel()
        {
            var builder = Kernel.CreateBuilder();

            // Dependencies required to construct AmaisKnowledgePlugin via ActivatorUtilities
            // when AddWkflowToolsAsync calls kernel.Plugins.AddFromType<AmaisKnowledgePlugin>.
            builder.Services.AddSingleton(Mock.Of<IEisKnowledgeProvider>());
            builder.Services.AddSingleton(Mock.Of<IChatNotificationService>());
            builder.Services.AddSingleton(new TenantContext());
            builder.Services.AddSingleton(new ChatContext());

            return builder.Build();
        }

        private static TenantTool BuildIntegrationAmaisTool(int id, string config, int? maxToolOutputLength = null) => new()
        {
            Id = id,
            Name = "Amais Integration",
            Type = "Integration_Amais",
            Config = config,
            IsActive = true,
            MaxToolOutputLength = maxToolOutputLength
        };

        private static Agent BuildAgent(Tool tool) => new TenantAgent
        {
            Id = 1,
            AgentTools = [new AgentTool { AgentId = 1, ToolId = tool.Id, Tool = tool }]
        };

        private static Task<bool> InvokeAsync(Kernel kernel, Agent agent, ChatContext chatContext = null) =>
            kernel.AddWkflowToolsAsync(
                agent,
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IChatNotificationService>(),
                Options.Create(new ElasticsearchSettings()),
                Mock.Of<IMcpClientService>(),
                new AMAIS.WKFLO.Common.Contexts.TenantContext(),
                chatContext ?? new ChatContext(),
                new AMAIS.WKFLO.Common.Contexts.UserContext(),
                agentTagNames: null,
                mapper: WkfloMapperExtensions.CreateMapper(),
                dateTimeProvider: Mock.Of<IDateTimeProvider>(),
                logger: NullLogger.Instance);

        [Fact]
        public async Task AddWkflowToolsAsync_AreSkillsEnabledTrue_ReturnsTrue()
        {
            var kernel = BuildKernel();
            var tool = BuildIntegrationAmaisTool(1, "{\"isVoiceEnabled\":false,\"areSkillsEnabled\":true}");
            var agent = BuildAgent(tool);

            var result = await InvokeAsync(kernel, agent);

            Assert.True(result);
        }

        [Fact]
        public async Task AddWkflowToolsAsync_AreSkillsEnabledFalse_ReturnsFalse()
        {
            var kernel = BuildKernel();
            var tool = BuildIntegrationAmaisTool(2, "{\"isVoiceEnabled\":false,\"areSkillsEnabled\":false}");
            var agent = BuildAgent(tool);

            var result = await InvokeAsync(kernel, agent);

            Assert.False(result);
        }

        [Fact]
        public async Task AddWkflowToolsAsync_MalformedConfig_AmaisIntegrationToolConfigurationIsNull_ReturnsFalse()
        {
            var kernel = BuildKernel();
            // Empty Config -> ToolResult.Mapping leaves AmaisIntegrationToolConfiguration null
            // (see ToolResult.cs: string.IsNullOrEmpty(src.Config) short-circuits the mapping).
            var tool = BuildIntegrationAmaisTool(3, string.Empty);
            var agent = BuildAgent(tool);

            var result = await InvokeAsync(kernel, agent);

            Assert.False(result);
        }

        [Fact]
        public async Task AddWkflowToolsAsync_NoAgentTools_ReturnsFalse()
        {
            var kernel = BuildKernel();
            var agent = new TenantAgent { Id = 1, AgentTools = null };

            var result = await InvokeAsync(kernel, agent);

            Assert.False(result);
        }

        [Fact]
        public async Task AddWkflowToolsAsync_AreSkillsEnabledTrue_RegistersAmaisKnowledgePlugin()
        {
            var kernel = BuildKernel();
            var tool = BuildIntegrationAmaisTool(4, "{\"isVoiceEnabled\":false,\"areSkillsEnabled\":true}");
            var agent = BuildAgent(tool);

            await InvokeAsync(kernel, agent);

            Assert.Contains(kernel.Plugins, p => p.Any(f => f.Name == AmaisKnowledgePlugin.GetSchoolKnowledgeToolName));
        }

        [Fact]
        public async Task AddWkflowToolsAsync_AreSkillsEnabledFalse_StillRegistersAmaisKnowledgePlugin()
        {
            var kernel = BuildKernel();
            var tool = BuildIntegrationAmaisTool(5, "{\"isVoiceEnabled\":false,\"areSkillsEnabled\":false}");
            var agent = BuildAgent(tool);

            await InvokeAsync(kernel, agent);

            Assert.Contains(kernel.Plugins, p => p.Any(f => f.Name == AmaisKnowledgePlugin.GetSchoolKnowledgeToolName));
        }

        [Fact]
        public async Task AddWkflowToolsAsync_MalformedConfig_StillRegistersAmaisKnowledgePlugin()
        {
            // Regression guard for Task 6972: previously registration was gated on
            // AmaisIntegrationToolConfiguration != null (plus a dead AmaisPluginCatalog.IsOfferable
            // check) — a malformed/empty Config used to skip AmaisKnowledgePlugin entirely.
            var kernel = BuildKernel();
            var tool = BuildIntegrationAmaisTool(6, string.Empty);
            var agent = BuildAgent(tool);

            await InvokeAsync(kernel, agent);

            Assert.Contains(kernel.Plugins, p => p.Any(f => f.Name == AmaisKnowledgePlugin.GetSchoolKnowledgeToolName));
        }

        [Fact]
        public async Task AddWkflowToolsAsync_ToolHasMaxToolOutputLength_RegistersLargeToolOutputFilter()
        {
            var kernel = BuildKernel();
            var tool = BuildIntegrationAmaisTool(7, "{\"isVoiceEnabled\":false,\"areSkillsEnabled\":false}", maxToolOutputLength: 500);
            var agent = BuildAgent(tool);

            await InvokeAsync(kernel, agent);

            Assert.Contains(kernel.FunctionInvocationFilters, f => f is SemanticKernelLargeToolOutputFilter);
        }

        [Fact]
        public async Task AddWkflowToolsAsync_NoToolHasMaxToolOutputLength_DoesNotRegisterLargeToolOutputFilter()
        {
            // MaxToolOutputLength null is the DB default (RunableTool.MaxToolOutputLength) — the filter
            // must be opt-in per tool, not always present, so a turn where nothing configured it doesn't
            // pay for an inert filter on every tool call.
            var kernel = BuildKernel();
            var tool = BuildIntegrationAmaisTool(8, "{\"isVoiceEnabled\":false,\"areSkillsEnabled\":false}", maxToolOutputLength: null);
            var agent = BuildAgent(tool);

            await InvokeAsync(kernel, agent);

            Assert.DoesNotContain(kernel.FunctionInvocationFilters, f => f is SemanticKernelLargeToolOutputFilter);
        }

        // --- Task 7152: MCP OAuth2 tools resolve the requesting user id for PerUser scope ------------------

        private static readonly Guid McpConnectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        private static TenantTool BuildMcpTool(int id, string oAuthScope) => new()
        {
            Id = id,
            Name = "Canva",
            Type = "MCP",
            Config = $"{{\"transportType\":\"Http\",\"endpoint\":\"https://mcp.canva.com/mcp\",\"authType\":\"OAuth2\",\"oAuthScope\":\"{oAuthScope}\"}}",
            IsActive = true,
            McpOAuthConnectionId = McpConnectionId
        };

        private static Task<bool> InvokeWithMcpAsync(Kernel kernel, Agent agent, Mock<IMcpClientService> mcpService, UserContext userContext) =>
            kernel.AddWkflowToolsAsync(
                agent,
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IChatNotificationService>(),
                Options.Create(new ElasticsearchSettings()),
                mcpService.Object,
                new AMAIS.WKFLO.Common.Contexts.TenantContext(),
                new ChatContext(),
                userContext,
                agentTagNames: null,
                mapper: WkfloMapperExtensions.CreateMapper(),
                dateTimeProvider: Mock.Of<IDateTimeProvider>(),
                logger: NullLogger.Instance);

        [Fact]
        public async Task AddWkflowToolsAsync_McpPerUserTool_ResolvesRequestingUserIdFromUserContext()
        {
            // Regression test: AddWkflowMcpToolAsync used to call AddServerConfig with no userId at all,
            // so a PerUser tool (e.g. Canva) could never find the token an admin already connected via the
            // dashboard. UserContext.Guid is already reliably populated here for any normally-authenticated
            // caller (e.g. an admin testing the agent) via the AI API's own UserContextResolver +
            // AutoUserCreationMiddleware pipeline — nothing about this needed Bug 7163/Task 7164 first.
            var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var kernel = BuildKernel();
            var tool = BuildMcpTool(9, "PerUser");
            var agent = BuildAgent(tool);
            var mcpService = new Mock<IMcpClientService>();
            mcpService.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await InvokeWithMcpAsync(kernel, agent, mcpService, new UserContext { Guid = userId.ToString() });

            mcpService.Verify(x => x.AddServerConfig(
                It.IsAny<McpServerConfig>(),
                McpConnectionId,
                userId), Times.Once());
        }

        [Fact]
        public async Task AddWkflowToolsAsync_McpSharedTool_NeverResolvesAUserId()
        {
            // A Shared tool (e.g. Humantic) must keep using the one tenant-wide row regardless of who's
            // chatting — McpOAuthScopeResolver only resolves a user id for PerUser-scoped tools.
            var kernel = BuildKernel();
            var tool = BuildMcpTool(10, "Shared");
            var agent = BuildAgent(tool);
            var mcpService = new Mock<IMcpClientService>();
            mcpService.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await InvokeWithMcpAsync(kernel, agent, mcpService, new UserContext { Guid = Guid.NewGuid().ToString() });

            mcpService.Verify(x => x.AddServerConfig(
                It.IsAny<McpServerConfig>(),
                McpConnectionId,
                null), Times.Once());
        }

        [Fact]
        public async Task AddWkflowToolsAsync_McpPerUserTool_NoResolvedUserContextGuid_SkipsTheToolWithoutThrowing()
        {
            // The public chat widget still resolves UserContext.Guid to empty (Bug 7163/Task 7164).
            // McpOAuthScopeResolver throws McpOAuthPerUserIdentityUnavailableException rather than silently
            // resolving null for this case — null is also the exact key a Shared row is stored under, so
            // resolving to it here could let this call silently read/use a stray Shared row for the same
            // connection (e.g. left over from before the tool was switched to PerUser) instead of correctly
            // finding no token at all. AddWkflowMcpToolAsync must catch that exception, skip loading this one
            // tool, and NOT call AddServerConfig at all — degrading gracefully rather than breaking tool
            // loading for every other tool the agent has.
            var kernel = BuildKernel();
            var tool = BuildMcpTool(11, "PerUser");
            var agent = BuildAgent(tool);
            var mcpService = new Mock<IMcpClientService>();
            mcpService.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await InvokeWithMcpAsync(kernel, agent, mcpService, new UserContext { Guid = null });

            mcpService.Verify(x => x.AddServerConfig(
                It.IsAny<McpServerConfig>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>()), Times.Never());
        }

        // --- Task 7130 addendum: chat-time MCP OAuth "reconnect" notification -----------------------------
        // Task 7130 addendum 2: reconnect is now a ChannelMessageType.Forms button (not a bare link), since
        // /mcp/oauth/start requires AmaisJwtOnly and a plain clicked link can't carry an Authorization header.

        private const string ExpectedReconnectStartPath = "/ai/api/v1/agent/share-abc/tool/11111111-1111-1111-1111-111111111111/mcp/oauth/start";

        private static Task<bool> InvokeWithMcpReconnectAsync(
            Kernel kernel, Agent agent, Mock<IMcpClientService> mcpService, Mock<IChatNotificationService> notificationService,
            UserContext userContext, AgentSharableContext agentSharableContext) =>
            kernel.AddWkflowToolsAsync(
                agent,
                Mock.Of<IDatastreamProvider>(),
                notificationService.Object,
                Options.Create(new ElasticsearchSettings()),
                mcpService.Object,
                new AMAIS.WKFLO.Common.Contexts.TenantContext(),
                new ChatContext { SessionId = "session-1" },
                userContext,
                agentTagNames: null,
                mapper: WkfloMapperExtensions.CreateMapper(),
                dateTimeProvider: Mock.Of<IDateTimeProvider>(),
                logger: NullLogger.Instance,
                agentSharableContext: agentSharableContext);

        [Fact]
        public async Task AddWkflowToolsAsync_McpTool_InteractiveConsentRequired_RealUserAndShareId_SendsReconnectButton()
        {
            // Task 7130 addendum: when the silent/background OAuth path throws
            // McpOAuthInteractiveConsentRequiredException (no cached token, needs a human to reconnect), and
            // this is a public-widget session (AgentSharableContext.ShareId set) with a real WKFLO user
            // identity (the new /mcp/oauth/start endpoint is AmaisJwtOnly), a live-only Forms chat
            // notification carrying the reconnect button data must be sent.
            var kernel = BuildKernel();
            var tool = BuildMcpTool(12, "Shared");
            var agent = BuildAgent(tool);
            var mcpService = new Mock<IMcpClientService>();
            mcpService.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new McpOAuthInteractiveConsentRequiredException("needs interactive consent"));
            var notificationService = new Mock<IChatNotificationService>();
            var userContext = new UserContext { Guid = Guid.NewGuid().ToString() };
            var agentSharableContext = new AgentSharableContext { ShareId = "share-abc" };

            await InvokeWithMcpReconnectAsync(kernel, agent, mcpService, notificationService, userContext, agentSharableContext);

            notificationService.Verify(x => x.SendMessageToChat(
                "session-1",
                It.Is<ChatMessage>(m =>
                    m.ChannelMessageType == ChannelMessageType.Forms
                    && (bool)m.Metadata[CommonMetadataConsts.RenderAsSystem]
                    && (string)m.Metadata[CommonMetadataConsts.FormType] == "McpOAuthReconnect"
                    && IsExpectedReconnectData((string)m.Metadata[CommonMetadataConsts.Data])),
                agent.Id,
                agent.Name,
                null), Times.Once());
        }

        private static bool IsExpectedReconnectData(string json)
        {
            var update = JsonConvert.DeserializeObject<McpOAuthReconnectUpdate>(json);
            return update != null
                && update.ToolName == "Canva"
                && update.StartPath == ExpectedReconnectStartPath
                && !string.IsNullOrWhiteSpace(update.Message);
        }

        [Theory]
        [InlineData(false, true)]  // no real user identity (anonymous share-only session)
        [InlineData(true, false)]  // no shareId (tenant-authenticated session, not the public widget)
        public async Task AddWkflowToolsAsync_McpTool_InteractiveConsentRequired_MissingPrerequisite_SendsFallbackTextInstead(
            bool hasRealUserIdentity, bool hasShareId)
        {
            // Any of these missing means the new AmaisJwtOnly-gated /mcp/oauth/start endpoint either doesn't
            // apply (no shareId — a tenant session, which already has its own dashboard Connect button) or
            // would 401 (no real user identity) — a dead button must never be sent; a plain-text fallback
            // should go out instead.
            var kernel = BuildKernel();
            var tool = BuildMcpTool(13, "Shared");
            var agent = BuildAgent(tool);
            var mcpService = new Mock<IMcpClientService>();
            mcpService.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new McpOAuthInteractiveConsentRequiredException("needs interactive consent"));
            var notificationService = new Mock<IChatNotificationService>();
            var userContext = new UserContext { Guid = hasRealUserIdentity ? Guid.NewGuid().ToString() : null };
            var agentSharableContext = new AgentSharableContext { ShareId = hasShareId ? "share-abc" : null };

            await InvokeWithMcpReconnectAsync(kernel, agent, mcpService, notificationService, userContext, agentSharableContext);

            notificationService.Verify(x => x.SendMessageToChat(
                "session-1",
                It.Is<ChatMessage>(m =>
                    m.ChannelMessageType == ChannelMessageType.Metadata
                    && !m.Metadata.ContainsKey(CommonMetadataConsts.FormType)
                    && !m.Metadata.ContainsKey(CommonMetadataConsts.Data)),
                agent.Id,
                agent.Name,
                null), Times.Once());
        }

        [Fact]
        public async Task AddWkflowToolsAsync_McpTool_PerUserIdentityUnavailable_SendsNoNotification()
        {
            // Unchanged existing behavior: McpOAuthPerUserIdentityUnavailableException is a different,
            // silent case (this tool is simply skipped, not surfaced to the chat user) — must not regress
            // into sending a reconnect notification too.
            var kernel = BuildKernel();
            var tool = BuildMcpTool(14, "PerUser");
            var agent = BuildAgent(tool);
            var mcpService = new Mock<IMcpClientService>();
            mcpService.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var notificationService = new Mock<IChatNotificationService>();
            var agentSharableContext = new AgentSharableContext { ShareId = "share-abc" };

            await InvokeWithMcpReconnectAsync(kernel, agent, mcpService, notificationService, new UserContext { Guid = null }, agentSharableContext);

            notificationService.Verify(x => x.SendMessageToChat(
                It.IsAny<string>(), It.IsAny<ChatMessage>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Fact]
        public async Task AddWkflowToolsAsync_McpTool_OrdinaryConnectionFailure_SendsNoNotification()
        {
            // Unchanged existing behavior: an unrelated connection failure (network error, etc.) is not a
            // "reconnect me" situation — must not regress into sending a reconnect notification too.
            var kernel = BuildKernel();
            var tool = BuildMcpTool(15, "Shared");
            var agent = BuildAgent(tool);
            var mcpService = new Mock<IMcpClientService>();
            mcpService.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var notificationService = new Mock<IChatNotificationService>();
            var agentSharableContext = new AgentSharableContext { ShareId = "share-abc" };

            await InvokeWithMcpReconnectAsync(kernel, agent, mcpService, notificationService, new UserContext { Guid = Guid.NewGuid().ToString() }, agentSharableContext);

            notificationService.Verify(x => x.SendMessageToChat(
                It.IsAny<string>(), It.IsAny<ChatMessage>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }
    }
}
