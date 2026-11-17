using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ConversationStarted;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Events.ConversationStarted
{
    /// <summary>
    /// This handler reacts to <see cref="ConversationStartedDomainEvent"/>, which
    /// <c>UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler.SyncConversationAsync</c> publishes exactly
    /// once per conversation, right after it inserts the first <c>Conversation</c> row for a session. The
    /// event firing at all IS the "new conversation" signal — there is no message-count or row-existence check
    /// left in this handler to test; that responsibility moved to the publisher (covered in
    /// <c>UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandlerTests</c>). These tests only cover this
    /// handler's own remaining logic: resolving the active DXP integration and reporting through it.
    /// </summary>
    public class ReportConversationStartedDomainEventHandlerTests
    {
        private readonly Mock<IDxpChatbotAnalyticsService> _dxpServiceMock;

        private const string TenantName = "test-tenant";
        private const string SessionId = "sess-1";
        private const string SubjectId = "subj-1";

        public ReportConversationStartedDomainEventHandlerTests()
        {
            _dxpServiceMock = new Mock<IDxpChatbotAnalyticsService>();
            _dxpServiceMock
                .Setup(x => x.ReportConversationStartedAsync(It.IsAny<DxpChatbotEventContext>(), It.IsAny<DateTimeOffset?>()))
                .ReturnsAsync(true);
        }

        private ReportConversationStartedDomainEventHandler BuildHandler(
            IEnumerable<DxpIntegration> integrations, IEnumerable<AgentChatSetting> chatSettings = null)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.DxpIntegrations).Returns(integrations.ToList().BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns((chatSettings ?? []).ToList().BuildMockDbSet().Object);

            return new ReportConversationStartedDomainEventHandler(
                db.Object, new TenantContext { TenantName = TenantName }, new ChatContext { SubjectId = SubjectId }, _dxpServiceMock.Object);
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

        private static ConversationStartedDomainEvent BuildNotification(RunnableAgent agent, DateTimeOffset? conversationDate = null) => new(agent, SessionId, conversationDate);

        [Fact]
        public async Task Handle_ReportsConversationStarted()
        {
            var agent = new TenantAgent { Id = 5, EnableSTM = true };
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = BuildNotification(agent);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationStartedAsync(
                It.Is<DxpChatbotEventContext>(c => c.SessionId == SessionId && c.SubjectId == SubjectId && c.TenantAgentId == agent.Id)),
                Times.Once);
        }

        [Fact]
        public async Task Handle_EnableSTMFalse_StillReports()
        {
            // The baseline signal must NOT depend on EnableSTM — this is the exact coverage gap it closes.
            // ConversationStartedDomainEvent is published unconditionally by SyncConversationAsync regardless
            // of EnableSTM, so this handler has nothing EnableSTM-specific to guard against, but the assertion
            // is kept as a regression guard for that upstream guarantee.
            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = BuildNotification(agent);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationStartedAsync(It.IsAny<DxpChatbotEventContext>()), Times.Once);
        }

        [Fact]
        public async Task Handle_NoActiveIntegration_DoesNotReport()
        {
            var agent = new TenantAgent { Id = 5, EnableSTM = true };
            var handler = BuildHandler(Array.Empty<DxpIntegration>());
            var notification = BuildNotification(agent);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationStartedAsync(It.IsAny<DxpChatbotEventContext>()), Times.Never);
        }

        [Fact]
        public async Task Handle_MultipleMatchingIntegrationRows_DeterministicallyPicksHighestId()
        {
            // Regression test: DxpIntegration.TenantId/Tenant.Name are uniquely constrained in the schema, so
            // in practice only one row should ever match, but FirstOrDefaultAsync without an ORDER BY has no
            // guaranteed row order if that invariant is ever violated (e.g. duplicate/stale rows from an
            // environment where the constraint hasn't been deployed). The handler must consistently resolve
            // the most recently created (highest Id) row rather than whichever happens to come back first.
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

            var agent = new TenantAgent { Id = 5, EnableSTM = true };
            // Deliberately inserted in an order where the stale row would be returned first by a naive query.
            var handler = BuildHandler([staleIntegration, currentIntegration]);
            var notification = BuildNotification(agent);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationStartedAsync(
                It.Is<DxpChatbotEventContext>(c => c.DxpTenantName == "dxp-tenant" && c.SessionId == SessionId)),
                Times.Once);
        }

        [Fact]
        public async Task Handle_PublishedTenantAgent_ResolvesPublishedTenantAgentId()
        {
            var agent = new PublishedTenantAgent { Id = 9, EnableSTM = true, CreatedByTenantAgentId = 3 };
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = BuildNotification(agent);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationStartedAsync(
                It.Is<DxpChatbotEventContext>(c => c.PublishedTenantAgentId == 9 && c.TenantAgentId == 3)),
                Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationCarriesConversationDate_ForwardsItToDxpService()
        {
            var agent = new TenantAgent { Id = 5, EnableSTM = true };
            var handler = BuildHandler([BuildActiveIntegration()]);
            var conversationDate = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
            var notification = BuildNotification(agent, conversationDate);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationStartedAsync(
                It.IsAny<DxpChatbotEventContext>(), conversationDate), Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationHasNoConversationDate_ForwardsNullGracefully()
        {
            var agent = new TenantAgent { Id = 5, EnableSTM = true };
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = BuildNotification(agent);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportConversationStartedAsync(
                It.IsAny<DxpChatbotEventContext>(), null), Times.Once);
        }
    }
}
