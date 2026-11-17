using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.MessageFeedbackRated
{
    public class ReportMessageFeedbackRatedDomainEventHandler : INotificationHandler<MessageFeedbackRatedDomainEvent>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IDxpChatbotAnalyticsService _dxpChatbotAnalyticsService;

        public ReportMessageFeedbackRatedDomainEventHandler(
            IMasterDataDbContext db,
            IDxpChatbotAnalyticsService dxpChatbotAnalyticsService)
        {
            _db = db;
            _dxpChatbotAnalyticsService = dxpChatbotAnalyticsService;
        }

        public async ValueTask Handle(MessageFeedbackRatedDomainEvent notification, CancellationToken cancellationToken)
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

            await _dxpChatbotAnalyticsService.ReportMessageFeedbackAsync(
                context, notification.MessageId, notification.IsThumbsUp,
                notification.TopicId, notification.TopicName, notification.MessageDate, notification.ConversationDate,
                notification.TopicVersion);
        }
    }
}
