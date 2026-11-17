using AMAIS.WKFLO.AI.Application.Agents.Events.MessageFeedbackRated;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Events.MessageFeedbackRated
{
    public class ReportMessageFeedbackRatedDomainEventHandlerTests
    {
        private readonly Mock<IDxpChatbotAnalyticsService> _dxpServiceMock;

        private const string TenantName = "test-tenant";
        private const string SessionId = "sess-1";
        private const string MessageId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

        public ReportMessageFeedbackRatedDomainEventHandlerTests()
        {
            _dxpServiceMock = new Mock<IDxpChatbotAnalyticsService>();
            _dxpServiceMock
                .Setup(x => x.ReportMessageFeedbackAsync(
                    It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>()))
                .ReturnsAsync(true);
        }

        private ReportMessageFeedbackRatedDomainEventHandler BuildHandler(
            IEnumerable<DxpIntegration> integrations, IEnumerable<AgentChatSetting> chatSettings = null)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.DxpIntegrations).Returns(integrations.ToList().BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns((chatSettings ?? []).ToList().BuildMockDbSet().Object);
            return new ReportMessageFeedbackRatedDomainEventHandler(db.Object, _dxpServiceMock.Object);
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
        public async Task Handle_IntegrationActive_ReportsFeedback()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = new MessageFeedbackRatedDomainEvent(TenantName, SessionId, "subj-1", 5, null, MessageId, true);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.Is<DxpChatbotEventContext>(c => c.SessionId == SessionId && c.SubjectId == "subj-1" && c.TenantAgentId == 5),
                MessageId, true), Times.Once);
        }

        [Fact]
        public async Task Handle_NoActiveIntegration_DoesNotReport()
        {
            var handler = BuildHandler(Array.Empty<DxpIntegration>());
            var notification = new MessageFeedbackRatedDomainEvent(TenantName, SessionId, "subj-1", 5, null, MessageId, true);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
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
            var notification = new MessageFeedbackRatedDomainEvent(TenantName, SessionId, "subj-1", 5, null, MessageId, true);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.Is<DxpChatbotEventContext>(c => c.DxpTenantName == "dxp-tenant"),
                MessageId, true), Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationCarriesTopicAndDates_ForwardsThemToDxpService()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var topicId = Guid.NewGuid();
            var messageDate = new DateTimeOffset(2026, 1, 5, 9, 55, 0, TimeSpan.Zero);
            var conversationDate = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
            var notification = new MessageFeedbackRatedDomainEvent(
                TenantName, SessionId, "subj-1", 5, null, MessageId, true,
                isTestChat: false, topicId: topicId, topicName: "Billing", messageDate: messageDate, conversationDate: conversationDate);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), MessageId, true, topicId, "Billing", messageDate, conversationDate), Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationHasNoTopicOrDates_ForwardsNullsGracefully()
        {
            // Classification may never have run for this message — a legitimate "uncorrelated" state, not an
            // error. The handler must still report the feedback event, just without the topic/date fields.
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = new MessageFeedbackRatedDomainEvent(TenantName, SessionId, "subj-1", 5, null, MessageId, true);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), MessageId, true, null, null, null, null, null), Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationCarriesTopicVersion_ForwardsItToDxpService()
        {
            // US 7142: TopicVersion is read off the already-loaded classification row by the publisher and
            // must reach ReportMessageFeedbackAsync unchanged.
            var handler = BuildHandler([BuildActiveIntegration()]);
            var topicId = Guid.NewGuid();
            var notification = new MessageFeedbackRatedDomainEvent(
                TenantName, SessionId, "subj-1", 5, null, MessageId, true,
                isTestChat: false, topicId: topicId, topicName: "Billing", topicVersion: "topic-version-4");

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), MessageId, true, topicId, "Billing", null, null, "topic-version-4"), Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationHasNoTopicVersion_ForwardsNullGracefully()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = new MessageFeedbackRatedDomainEvent(TenantName, SessionId, "subj-1", 5, null, MessageId, true, topicVersion: null);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), MessageId, true, null, null, null, null, null), Times.Once);
        }
    }
}
