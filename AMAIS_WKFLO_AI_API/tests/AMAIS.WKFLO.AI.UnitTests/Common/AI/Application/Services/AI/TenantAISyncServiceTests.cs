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
    /// <summary>
    /// wkflo_ui's shared `use-chat.ts` hook is used by both the public widget and the authenticated
    /// tenant-facing app, so TenantAISyncService.RunTenantAgentByIdAsync needs to forward
    /// IsContactFormReplay exactly like PublicAISyncService.RunPublicAgentByShareIdAsync does — see
    /// PublicAISyncServiceTests for the public-side equivalent of this test.
    /// </summary>
    public class TenantAISyncServiceTests
    {
        private const string TenantName = "test-tenant";
        private const string SessionId = "session-1";
        private const int AgentId = 5;

        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly Mock<IRunnableAISyncService> _runnableAISyncServiceMock;
        private readonly TenantContext _tenantContext;

        public TenantAISyncServiceTests()
        {
            _notificationServiceMock = new Mock<IChatNotificationService>();
            _notificationServiceMock
                .Setup(x => x.SendConversationStateUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns(Task.CompletedTask);

            _runnableAISyncServiceMock = new Mock<IRunnableAISyncService>();
            _tenantContext = new TenantContext { TenantName = TenantName };
        }

        private TenantAISyncService BuildService(TenantAgent agent)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent> { agent }.BuildMockDbSet().Object);

            return new TenantAISyncService(db.Object, _notificationServiceMock.Object, _runnableAISyncServiceMock.Object, Mock.Of<IDateTimeProvider>(), _tenantContext);
        }

        private static TenantAgent BuildAgent()
        {
            var tenant = new Tenant { Id = 1, Name = TenantName };
            return new TenantAgent { Id = AgentId, Name = "Support Bot", Tenant = tenant, IsActive = true };
        }

        [Fact]
        public async Task RunTenantAgentByIdAsync_IsContactFormReplayTrue_ForwardsIsContactFormReplayTrue()
        {
            // The flag set by wkflo_ui on a ContactForm "replay" turn must reach
            // RunnableAISyncService.RunRunnableAgentAsync unchanged, since that's the one place it gets
            // threaded further down into RunTextOptions.IsContactFormReplay — identical to the public
            // path's equivalent forwarding.
            var agent = BuildAgent();
            var service = BuildService(agent);

            _runnableAISyncServiceMock
                .Setup(x => x.RunRunnableAgentAsync(
                    It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("the real answer");

            await service.RunTenantAgentByIdAsync(AgentId, "what are your hours?", null, SessionId, chainOfThought: false, isContactFormReplay: true, cancellationToken: TestContext.Current.CancellationToken);

            _runnableAISyncServiceMock.Verify(x => x.RunRunnableAgentAsync(
                It.Is<RunnableAgent>(a => a.Id == AgentId), "what are your hours?", null, SessionId, false,
                "No provider found!", null, true, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RunTenantAgentByIdAsync_IsContactFormReplayOmitted_ForwardsIsContactFormReplayFalse()
        {
            // Regression guard: an ordinary (non-replay) tenant chat message must keep forwarding false.
            var agent = BuildAgent();
            var service = BuildService(agent);

            _runnableAISyncServiceMock
                .Setup(x => x.RunRunnableAgentAsync(
                    It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<List<FileAttachment>>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("agent reply");

            await service.RunTenantAgentByIdAsync(AgentId, "hello", null, SessionId, chainOfThought: false, cancellationToken: TestContext.Current.CancellationToken);

            _runnableAISyncServiceMock.Verify(x => x.RunRunnableAgentAsync(
                It.Is<RunnableAgent>(a => a.Id == AgentId), "hello", null, SessionId, false,
                "No provider found!", null, false, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
