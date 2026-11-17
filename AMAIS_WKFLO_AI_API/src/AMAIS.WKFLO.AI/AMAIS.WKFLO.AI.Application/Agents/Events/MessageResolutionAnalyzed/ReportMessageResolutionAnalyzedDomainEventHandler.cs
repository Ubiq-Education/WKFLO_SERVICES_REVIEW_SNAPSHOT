using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.MessageResolutionAnalyzed
{
    public class ReportMessageResolutionAnalyzedDomainEventHandler : INotificationHandler<MessageResolutionAnalyzedDomainEvent>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IDxpChatbotAnalyticsService _dxpChatbotAnalyticsService;

        public ReportMessageResolutionAnalyzedDomainEventHandler(
            IMasterDataDbContext db,
            IDxpChatbotAnalyticsService dxpChatbotAnalyticsService)
        {
            _db = db;
            _dxpChatbotAnalyticsService = dxpChatbotAnalyticsService;
        }

        public async ValueTask Handle(MessageResolutionAnalyzedDomainEvent notification, CancellationToken cancellationToken)
        {
            // OrderByDescending(Id): see ReportConversationStartedDomainEventHandler for why
            // this ordering exists — makes row selection deterministic if the TenantId/Tenant.Name uniqueness
            // invariant is ever violated, rather than relying on undefined FirstOrDefaultAsync ordering.
            var integration = await _db.DxpIntegrations
                .AsNoTracking()
                .Where(x => x.Tenant.Name == notification.TenantName && x.IsActive && !x.IsDeleted)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (integration == null)
                return;

            var shareId = await DxpAgentIdentityResolver.ResolveShareIdAsync(
                _db, notification.TenantAgentId, notification.PublishedTenantAgentId, cancellationToken);

            var context = new DxpChatbotEventContext(
                integration.DxpTenantName,
                integration.Domain,
                integration.ApiKey,
                integration.TenantId,
                notification.TenantName,
                notification.SessionId,
                notification.SubjectId,
                notification.TenantAgentId,
                notification.PublishedTenantAgentId,
                shareId,
                notification.IsTestChat);

            // US 7142: TopicVersion is now persisted on MessageTopicClassification and carried through on the
            // notification (read from classification.TopicVersion by the publisher) — no longer hardcoded null.
            await _dxpChatbotAnalyticsService.ReportMessageTopicAsync(
                context, notification.MessageId, notification.TopicId, notification.TopicName, notification.TopicVersion,
                notification.MessageDate, notification.ConversationDate,
                notification.IsResolved, notification.Question, notification.Response, notification.AnswerMessageId);
        }
    }
}
