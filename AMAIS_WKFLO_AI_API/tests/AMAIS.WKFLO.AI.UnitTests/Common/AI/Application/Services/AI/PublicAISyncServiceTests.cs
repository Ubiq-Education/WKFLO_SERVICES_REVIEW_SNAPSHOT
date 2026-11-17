using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Services.AI;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Common.AI.Application.Services.AI
{
    public class PublicAISyncServiceTests
    {
        private const string TenantName = "test-tenant";
        private const string ShareId = "share-1";
        private const string SessionId = "session-1";

        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly Mock<IRunnableAISyncService> _runnableAISyncServiceMock;
        private readonly TenantContext _tenantContext;

        private PublicAISyncService BuildService(AgentChatSetting chatSetting)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);

            return new PublicAISyncService(db.Object, _notificationServiceMock.Object, _runnableAISyncServiceMock.Object, _tenantContext, Mock.Of<IDateTimeProvider>());
        }

        public PublicAISyncServiceTests()
        {
            _notificationServiceMock = new Mock<IChatNotificationService>();
            _notificationServiceMock
                .Setup(x => x.SendConversationStateUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns(Task.CompletedTask);

            _runnableAISyncServiceMock = new Mock<IRunnableAISyncService>();
            _tenantContext = new TenantContext { TenantName = TenantName };
        }

        private static AgentChatSetting BuildChatSetting(bool isWidget)
        {
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new TenantAgent { Id = 5, Name = "Support Bot", Tenant = tenant, IsActive = true };
            return new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = 5, Agent = agent, IsWidget = isWidget, ChainOfThought = false };
        }

        [Fact]
        public async Task RunPublicAgentByShareIdAsync_ChatSettingIsWidget_ForwardsTrueAsRootIsWidget()
        {
            // The AgentChatSetting row matched by ShareId already carries the definitive widget/non-widget
            // context for this exact share link — no need to re-derive it via the AgentId heuristic
            // downstream (AgentEventPublisherPlugin's fallback), so it must be forwarded directly.
            var chatSetting = BuildChatSetting(isWidget: true);
            var service = BuildService(chatSetting);

            _runnableAISyncServiceMock
                .Setup(x => x.RunRunnableAgentAsync(
                    It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("agent reply");

            await service.RunPublicAgentByShareIdAsync(ShareId, "hello", null, SessionId, cancellationToken: TestContext.Current.CancellationToken);

            _runnableAISyncServiceMock.Verify(x => x.RunRunnableAgentAsync(
                It.Is<RunnableAgent>(a => a.Id == 5), "hello", null, SessionId, false,
                "AI provider not supported", true, false, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunPublicAgentByShareIdAsync_ChatSettingIsNotWidget_ForwardsFalseAsRootIsWidget()
        {
            var chatSetting = BuildChatSetting(isWidget: false);
            var service = BuildService(chatSetting);

            _runnableAISyncServiceMock
                .Setup(x => x.RunRunnableAgentAsync(
                    It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("agent reply");

            await service.RunPublicAgentByShareIdAsync(ShareId, "hello", null, SessionId, cancellationToken: TestContext.Current.CancellationToken);

            _runnableAISyncServiceMock.Verify(x => x.RunRunnableAgentAsync(
                It.Is<RunnableAgent>(a => a.Id == 5), "hello", null, SessionId, false,
                "AI provider not supported", false, false, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunPublicAgentByShareIdAsync_IsContactFormReplayTrue_ForwardsIsContactFormReplayTrue()
        {
            // The flag set by wkflo_ui on a ContactForm "replay" turn must reach
            // RunnableAISyncService.RunRunnableAgentAsync unchanged, since that's the one place it gets
            // threaded further down into RunTextOptions.IsContactFormReplay.
            var chatSetting = BuildChatSetting(isWidget: true);
            var service = BuildService(chatSetting);

            _runnableAISyncServiceMock
                .Setup(x => x.RunRunnableAgentAsync(
                    It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("the real answer");

            await service.RunPublicAgentByShareIdAsync(ShareId, "what are your hours?", null, SessionId, isContactFormReplay: true, cancellationToken: TestContext.Current.CancellationToken);

            _runnableAISyncServiceMock.Verify(x => x.RunRunnableAgentAsync(
                It.Is<RunnableAgent>(a => a.Id == 5), "what are your hours?", null, SessionId, false,
                "AI provider not supported", true, true, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunVoicePublicAgentByIdAsync_ChatSettingIsWidget_ForwardsTrueAsRootIsWidget()
        {
            var chatSetting = BuildChatSetting(isWidget: true);
            var service = BuildService(chatSetting);

            _runnableAISyncServiceMock
                .Setup(x => x.RunRunnableAgentVoiceAsync(
                    It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("agent reply");

            await service.RunVoicePublicAgentByIdAsync(ShareId, "hello", SessionId, branchId: "branch-1", chainOfThought: false, TestContext.Current.CancellationToken);

            _runnableAISyncServiceMock.Verify(x => x.RunRunnableAgentVoiceAsync(
                It.Is<RunnableAgent>(a => a.Id == 5), "hello", SessionId, "branch-1", false,
                "AI provider not supported", true, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunVoicePublicAgentByIdAsync_ChatSettingIsNotWidget_ForwardsFalseAsRootIsWidget()
        {
            var chatSetting = BuildChatSetting(isWidget: false);
            var service = BuildService(chatSetting);

            _runnableAISyncServiceMock
                .Setup(x => x.RunRunnableAgentVoiceAsync(
                    It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("agent reply");

            await service.RunVoicePublicAgentByIdAsync(ShareId, "hello", SessionId, branchId: "branch-1", chainOfThought: false, TestContext.Current.CancellationToken);

            _runnableAISyncServiceMock.Verify(x => x.RunRunnableAgentVoiceAsync(
                It.Is<RunnableAgent>(a => a.Id == 5), "hello", SessionId, "branch-1", false,
                "AI provider not supported", false, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
