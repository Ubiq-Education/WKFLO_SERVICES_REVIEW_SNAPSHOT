using AMAIS.WKFLO.AI.Application.Agents.Events.MessageResolutionAnalyzed;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Events.MessageResolutionAnalyzed
{
    public class ReportMessageResolutionAnalyzedDomainEventHandlerTests
    {
        private readonly Mock<IDxpChatbotAnalyticsService> _dxpServiceMock;

        private const string TenantName = "test-tenant";
        private const string SessionId = "sess-1";
        private const string MessageId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        private static readonly DateTimeOffset MessageDate = new(2026, 1, 5, 9, 55, 0, TimeSpan.Zero);

        public ReportMessageResolutionAnalyzedDomainEventHandlerTests()
        {
            _dxpServiceMock = new Mock<IDxpChatbotAnalyticsService>();
            _dxpServiceMock
                .Setup(x => x.ReportMessageTopicAsync(
                    It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
        }

        private ReportMessageResolutionAnalyzedDomainEventHandler BuildHandler(
            IEnumerable<DxpIntegration> integrations, IEnumerable<AgentChatSetting> chatSettings = null)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.DxpIntegrations).Returns(integrations.ToList().BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns((chatSettings ?? []).ToList().BuildMockDbSet().Object);
            return new ReportMessageResolutionAnalyzedDomainEventHandler(db.Object, _dxpServiceMock.Object);
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

        private static MessageResolutionAnalyzedDomainEvent BuildNotification(
            Guid? topicId = null, string topicName = null, string topicVersion = null, bool isResolved = true, string answerMessageId = null) => new(
                TenantName, SessionId, "subj-1", 5, null, MessageId,
                topicId, topicName, isResolved, "What are your hours?", "9 to 5.",
                isTestChat: false, MessageDate, conversationDate: null, topicVersion: topicVersion, answerMessageId: answerMessageId);

        [Fact]
        public async Task Handle_IntegrationActive_ReportsMessageTopicWithNotificationFields()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var topicId = Guid.NewGuid();
            var notification = BuildNotification(topicId, "Billing", "topic-version-3", isResolved: false);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.Is<DxpChatbotEventContext>(c => c.SessionId == SessionId && c.SubjectId == "subj-1" && c.TenantAgentId == 5),
                MessageId, topicId, "Billing", "topic-version-3",
                MessageDate, null, false, "What are your hours?", "9 to 5."), Times.Once);
        }

        [Fact]
        public async Task Handle_NoActiveIntegration_DoesNotReport()
        {
            var handler = BuildHandler(Array.Empty<DxpIntegration>());
            var notification = BuildNotification();

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_NotificationHasNoTopicVersion_ForwardsNullTopicVersion_NotHardcoded()
        {
            // US 7142: this used to always pass topicVersion: null hardcoded at the call site — now it must
            // forward whatever the notification carries, null included when the classification row never had one.
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = BuildNotification(topicVersion: null);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), MessageId, It.IsAny<Guid?>(), It.IsAny<string>(), null,
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationHasRealTopicVersion_ForwardsIt()
        {
            var handler = BuildHandler([BuildActiveIntegration()]);
            var notification = BuildNotification(topicVersion: "topic-version-5");

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), MessageId, It.IsAny<Guid?>(), It.IsAny<string>(), "topic-version-5",
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationHasAnswerMessageId_ForwardsIt()
        {
            // US 7142 (later re-scope): the answer's own MessageId - distinct from MessageId (the question)
            // this whole event is keyed by - must be forwarded so a later rating can look the classification
            // row up directly.
            var handler = BuildHandler([BuildActiveIntegration()]);
            var answerMessageId = Guid.NewGuid().ToString();
            var notification = BuildNotification(answerMessageId: answerMessageId);

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _dxpServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), MessageId, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>(),
                answerMessageId),
                Times.Once);
        }
    }
}
