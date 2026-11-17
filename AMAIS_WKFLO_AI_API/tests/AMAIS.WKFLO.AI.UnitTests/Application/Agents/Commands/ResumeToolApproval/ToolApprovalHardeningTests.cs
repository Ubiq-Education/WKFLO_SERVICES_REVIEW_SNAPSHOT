using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Settings;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>WI 7575 pre-PR hardening: resume respects the agent's current state and the original turn; fail closed.</summary>
    public class ToolApprovalHardeningTests
    {
        private static ToolApprovalRecord Approved(string shareId = null, bool? rootIsWidget = null)
        {
            var record = ToolApprovalHistoryTests.Record("Approved", "AllowOnce");
            record.ShareId = shareId;
            record.Chat = new ToolApprovalChatState { RootIsWidget = rootIsWidget };
            return record;
        }

        private static RunnableAISyncService Service(List<Agent> agents, List<AgentChatSetting> settings, ISemanticKernelAgentService semanticKernel = null)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(agents.BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns(settings.BuildMockDbSet().Object);
            db.Setup(x => x.EntityTags).Returns(new List<EntityTag>().BuildMockDbSet().Object);
            return new RunnableAISyncService(db.Object, WkfloMapperExtensions.CreateMapper(), semanticKernel ?? Mock.Of<ISemanticKernelAgentService>(),
                Mock.Of<IChatNotificationService>(), Mock.Of<IDateTimeProvider>(), new TenantContext());
        }

        private static TenantAgent Agent(bool isActive = true, bool isDeleted = false) => new()
        {
            Id = 5,
            Name = "Support Agent",
            IsActive = isActive,
            IsDeleted = isDeleted,
            Tenant = new Tenant { Name = "TestTenant" },
            AIModel = new AIModel { Id = 3, Provider = "openai", Model = "gpt-4o", Name = "model", Secret = new Secret { ApiKey = "test-key" } },
            AgentTools = new List<AgentTool>()
        };

        [Theory]
        [InlineData(true, false, null, true)]   // tenant chat, active
        [InlineData(false, false, null, false)] // switched off
        [InlineData(true, true, null, false)]   // deleted
        [InlineData(true, false, "share-1", true)]
        [InlineData(true, false, "revoked-share", false)]
        [InlineData(true, false, "share-of-other-agent", false)]
        public async Task CanResume_ChecksAgentStateAndShare(bool isActive, bool isDeleted, string shareId, bool expected)
        {
            var settings = new List<AgentChatSetting>
            {
                new() { Id = 1, ShareId = "share-1", AgentId = 5 },
                new() { Id = 2, ShareId = "revoked-share", AgentId = 5, IsDeleted = true },
                new() { Id = 3, ShareId = "share-of-other-agent", AgentId = 99 }
            };
            var service = Service(new List<Agent> { Agent(isActive, isDeleted) }, settings);

            Assert.Equal(expected, await service.CanResumeAsync(Approved(shareId), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ResumeAfterToolApproval_InactiveAgent_DoesNotRun()
        {
            var semanticKernel = new Mock<ISemanticKernelAgentService>();
            var service = Service(new List<Agent> { Agent(isActive: false) }, new List<AgentChatSetting>(), semanticKernel.Object);

            Assert.Null(await service.ResumeAfterToolApprovalAsync(Approved(), TestContext.Current.CancellationToken));
            semanticKernel.Verify(x => x.RunTextAsync(It.IsAny<RunTextOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [InlineData(null)]
        public async Task ResumeAfterToolApproval_PassesTheOriginalTurnsRootIsWidget(bool? rootIsWidget)
        {
            RunTextOptions seen = null;
            var semanticKernel = new Mock<ISemanticKernelAgentService>();
            semanticKernel.Setup(x => x.RunTextAsync(It.IsAny<RunTextOptions>(), It.IsAny<CancellationToken>()))
                .Callback<RunTextOptions, CancellationToken>((o, _) => seen = o)
                .ReturnsAsync(new AgentAnswer { MessageId = "m", Message = "ok", TokenUsage = new AITokenUsage() });
            var service = Service(new List<Agent> { Agent() }, new List<AgentChatSetting> { new() { Id = 1, ShareId = "share-1", AgentId = 5 } }, semanticKernel.Object);

            await service.ResumeAfterToolApprovalAsync(Approved("share-1", rootIsWidget), TestContext.Current.CancellationToken);

            Assert.Equal(rootIsWidget, seen.RootIsWidget);
        }

        [Fact]
        public async Task MarkRefused_TurnsApprovedIntoDenied_AndIgnoresOtherStates()
        {
            const string store = "statestore-ai";
            const string key = "tool-approval-approval-1";
            var dapr = new Mock<DaprClient>();
            dapr.Setup(x => x.GetStateAndETagAsync<ToolApprovalRecord>(store, key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Approved(), "etag-1"));
            dapr.Setup(x => x.TrySaveStateAsync(store, key, It.IsAny<ToolApprovalRecord>(), "etag-1", It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var service = new ToolApprovalStateService(dapr.Object);

            var refused = await service.MarkRefusedAsync("approval-1", DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

            Assert.Equal("Denied", refused.Status);
            Assert.Equal("Deny", refused.Decision);

            dapr.Setup(x => x.GetStateAndETagAsync<ToolApprovalRecord>(store, key, It.IsAny<ConsistencyMode?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ToolApprovalHistoryTests.Record("Expired", "Expired"), "etag-2"));
            Assert.Null(await service.MarkRefusedAsync("approval-1", DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
        }

        private static SemanticKernelAgentService AgentService(IToolApprovalStateService state, IToolApprovalExpiryScheduler scheduler) =>
            new(Mock.Of<ILogger<SemanticKernelAgentService>>(), Mock.Of<IWkfloMapper>(), Mock.Of<IDatastreamProvider>(), Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(), Mock.Of<IMcpClientService>(), Mock.Of<IWkfloMediator>(), Options.Create(new ElasticsearchSettings()),
                new TenantContext(), new ChatContext(), new UserContext(), Mock.Of<IBlobStorageProvider>(), Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(), Mock.Of<IMasterDataDbContext>(), Mock.Of<IContactFormPluginLogService>(),
                toolApprovalStateService: state, toolApprovalExpiryScheduler: scheduler);

        [Theory]
        [InlineData(true, true, true, true)]
        [InlineData(false, true, true, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, false, false)]
        public void ApprovalsAvailable_FailsClosed_WithoutInteractiveRunOrApprovalServices(bool interactive, bool hasState, bool hasScheduler, bool expected)
        {
            var service = AgentService(hasState ? Mock.Of<IToolApprovalStateService>() : null, hasScheduler ? Mock.Of<IToolApprovalExpiryScheduler>() : null);

            var available = service.ResolveApprovalsAvailable(interactive);

            Assert.Equal(expected, available);
            // ...and so Ask acts as Block whenever approvals are not available.
            var policy = ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(), available);
            Assert.Equal(expected ? "Ask" : "Block", policy.GetToolPermission(ToolPermissionPolicyTests.HttpToolId));
        }

        [Fact]
        public async Task ApprovalRequest_StoresTheOriginalTurnsChatState()
        {
            var state = new Mock<IToolApprovalStateService>();
            var chatContext = new ChatContext { SessionId = "session-1", PagePath = "/news", PageId = "4321", FeatureId = "9", FeatureType = "Gallery", SubjectId = "subject-1", IsTestChat = true, TimeZone = "UTC" };
            var runInfo = new ToolApprovalRunInfo("TestTenant", "user-1", "session-1", 100, "Support Agent", "share-1", null, Guid.NewGuid(),
                new Dictionary<int, string> { [2] = "Weather" }, ToolApprovalChatState.From(chatContext, rootIsWidget: true));
            var filter = new ToolApprovalAutoFunctionInvocationFilter(ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(), true), state.Object,
                Mock.Of<IToolApprovalExpiryScheduler>(), Mock.Of<IChatNotificationService>(), chatContext, Mock.Of<IDateTimeProvider>(), runInfo, NullLogger.Instance);
            var function = KernelFunctionFactory.CreateFromMethod(() => "ran", "call_http").Clone("plugin_2_HTTP");
            var context = new AutoFunctionInvocationContext(Kernel.CreateBuilder().Build(), function, new FunctionResult(function), new ChatHistory(),
                new ChatMessageContent(AuthorRole.Assistant, ""))
            { ToolCallId = "call-1" };

            await filter.OnAutoFunctionInvocationAsync(context, _ => Task.CompletedTask);

            var chat = filter.RequestedApproval.Chat;
            Assert.Equal("/news", chat.PagePath);
            Assert.Equal("4321", chat.PageId);
            Assert.Equal("9", chat.FeatureId);
            Assert.Equal("Gallery", chat.FeatureType);
            Assert.Equal("subject-1", chat.SubjectId);
            Assert.True(chat.IsTestChat);
            Assert.True(chat.RootIsWidget);

            var restored = chat.ToChatContext("session-1");
            Assert.Equal("session-1", restored.SessionId);
            Assert.Equal("/news", restored.PagePath);
            Assert.Equal("4321", restored.PageId);
            Assert.Equal("UTC", restored.TimeZone);
        }
    }
}
