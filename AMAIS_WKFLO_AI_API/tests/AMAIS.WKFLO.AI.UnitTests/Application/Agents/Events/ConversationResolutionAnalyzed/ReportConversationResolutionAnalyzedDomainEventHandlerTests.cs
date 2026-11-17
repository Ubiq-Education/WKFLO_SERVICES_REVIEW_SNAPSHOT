using AMAIS.WKFLO.AI.Application.Agents.Events.ConversationResolutionAnalyzed;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Events.ConversationResolutionAnalyzed
{
    public class ReportConversationResolutionAnalyzedDomainEventHandlerTests
    {
        private readonly Mock<IDxpChatbotAnalyticsService> _dxpServiceMock;

        private const string TenantName = "test-tenant";
        private const string SessionId = "sess-1";

        public ReportConversationResolutionAnalyzedDomainEventHandlerTests()
        {
            _dxpServiceMock = new Mock<IDxpChatbotAnalyticsService>();
            _dxpServiceMock
                .Setup(x => x.ReportConversationResolutionAsync(
                    It.IsAny<DxpChatbotEventContext>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>()))
                .ReturnsAsync(true);
        }

        private ReportConversationResolutionAnalyzedDomainEventHandler BuildHandler(
            IEnumerable<DxpIntegration> integrations, IEnumerable<AgentChatSetting> chatSettings = null)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.DxpIntegrations).Returns(integrations.ToList().BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns((chatSettings ?? []).ToList().BuildMockDbSet().Object);
            return new ReportConversationResolutionAnalyzedDomainEventHandler(db.Object, _dxpServiceMock.Object);
        }

        private static DxpIntegration BuildActiveIntegration() => new()
        {
            Id = 1,
            TenantId = 1,
            Tenant = new Tenant { Id = 1, Name = TenantName },
            DxpTenantName = "dxp-tenant",
            Domain = "https://dxp.example.com",
            ApiKey = "api-key",
            IsActive = true,
            IsDeleted = false
        };

        [Fact]
        public async Task Handle_IntegrationActive_ReportsResolution()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var analyzedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var notification = new ConversationResolutionAnalyzedDomainEvent(TenantName, SessionId, "subj-1", 5, null, true, analyzedAt);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationResolutionAsync(
                It.Is<DxpChatbotEventContext>(c => c.SessionId == SessionId && c.SubjectId == "subj-1" && c.TenantAgentId == 5),
                true, analyzedAt), Times.Once);
        }

        [Fact]
        public async Task Handle_NoActiveIntegration_DoesNotReport()
        {
            var handler = BuildHandler(Array.Empty<DxpIntegration>());
            var notification = new ConversationResolutionAnalyzedDomainEvent(TenantName, SessionId, "subj-1", 5, null, true, DateTimeOffset.UtcNow);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationResolutionAsync(
                It.IsAny<DxpChatbotEventContext>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset>()), Times.Never);
        }

        [Fact]
        public async Task Handle_MultipleMatchingIntegrationRows_DeterministicallyPicksHighestId()
        {
            // Regression test: see ReportConversationStartedDomainEventHandlerTests for the
            // full rationale — FirstOrDefaultAsync without ORDER BY has no guaranteed row order if the
            // TenantId/Tenant.Name uniqueness invariant is ever violated (duplicate/stale rows).
            var staleIntegration = new DxpIntegration
            {
                Id = 1,
                TenantId = 1,
                Tenant = new Tenant { Id = 1, Name = TenantName },
                DxpTenantName = string.Empty,
                Domain = string.Empty,
                ApiKey = string.Empty,
                IsActive = true,
                IsDeleted = false
            };
            var currentIntegration = new DxpIntegration
            {
                Id = 2,
                TenantId = 1,
                Tenant = new Tenant { Id = 1, Name = TenantName },
                DxpTenantName = "dxp-tenant",
                Domain = "https://dxp.example.com",
                ApiKey = "api-key",
                IsActive = true,
                IsDeleted = false
            };

            var handler = BuildHandler([staleIntegration, currentIntegration]);
            var notification = new ConversationResolutionAnalyzedDomainEvent(TenantName, SessionId, "subj-1", 5, null, true, DateTimeOffset.UtcNow);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationResolutionAsync(
                It.Is<DxpChatbotEventContext>(c => c.DxpTenantName == "dxp-tenant"),
                true, It.IsAny<DateTimeOffset>()), Times.Once);
        }

        [Fact]
        public async Task Handle_UnresolvedConversation_ReportsIsResolvedFalse()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = new ConversationResolutionAnalyzedDomainEvent(TenantName, SessionId, "subj-1", 5, null, false, DateTimeOffset.UtcNow);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationResolutionAsync(
                It.IsAny<DxpChatbotEventContext>(), false, It.IsAny<DateTimeOffset>()), Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationCarriesConversationDate_ForwardsItToDxpService()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var analyzedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var conversationDate = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);
            var notification = new ConversationResolutionAnalyzedDomainEvent(
                TenantName, SessionId, "subj-1", 5, null, true, analyzedAt, isTestChat: false, conversationDate: conversationDate);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationResolutionAsync(
                It.IsAny<DxpChatbotEventContext>(), true, analyzedAt, conversationDate), Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationHasNoConversationDate_ForwardsNullGracefully()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var analyzedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var notification = new ConversationResolutionAnalyzedDomainEvent(TenantName, SessionId, "subj-1", 5, null, true, analyzedAt);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationResolutionAsync(
                It.IsAny<DxpChatbotEventContext>(), true, analyzedAt, null), Times.Once);
        }
    }
}
