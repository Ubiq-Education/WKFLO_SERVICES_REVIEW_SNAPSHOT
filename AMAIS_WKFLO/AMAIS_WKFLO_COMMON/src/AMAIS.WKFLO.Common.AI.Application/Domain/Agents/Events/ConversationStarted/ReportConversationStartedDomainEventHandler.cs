using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ConversationStarted
{
    /// <summary>
    /// Reports the "ChatBot Conversation" DXP analytics event (reused from the legacy in-house chatbot, for
    /// continuity) — the guaranteed baseline signal DXP uses (via distinct-correlationId cardinality
    /// aggregation) to count conversations, independent of whether resolution analysis, message feedback, or
    /// topic classification ever produce a signal for a given session.
    /// </summary>
    /// <remarks>
    /// This handler reacts to <see cref="ConversationStartedDomainEvent"/>, which fires exactly once per
    /// conversation, at the source (see that event's own doc comment). A session whose conversation was deleted and
    /// then resumed fires it again; DXP counts distinct correlation ids, so that does not double-count. There is deliberately no
    /// "is this the first turn" check here — the event firing at all already answers that question, with no
    /// ambiguity and no dependency on message shape or on dispatch order relative to any other handler. An
    /// earlier version of this handler subscribed to the generic <c>ChatHistoryUpdatedDomainEvent</c> instead
    /// and re-derived "first turn" itself — first by counting User/Assistant messages (broken: a first turn
    /// with an attachment or non-empty Chain-of-Thought reasoning can add extra messages), then by querying
    /// <c>Conversations</c> for row existence directly (fragile: only correct because of an undocumented,
    /// coincidental Mediator handler dispatch order). Both were replaced by having
    /// <c>UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler.SyncConversationAsync</c> — the one place
    /// that already knows with certainty when a <c>Conversation</c> row is genuinely new — publish this event
    /// itself, once, at the source.
    /// </remarks>
    public class ReportConversationStartedDomainEventHandler : INotificationHandler<ConversationStartedDomainEvent>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly IDxpChatbotAnalyticsService _dxpChatbotAnalyticsService;

        public ReportConversationStartedDomainEventHandler(
            IMasterDataDbContext db,
            TenantContext tenantContext,
            ChatContext chatContext,
            IDxpChatbotAnalyticsService dxpChatbotAnalyticsService)
        {
            _db = db;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _dxpChatbotAnalyticsService = dxpChatbotAnalyticsService;
        }

        public async ValueTask Handle(ConversationStartedDomainEvent notification, CancellationToken cancellationToken)
        {
            // OrderByDescending(Id): DxpIntegration.TenantId is uniquely constrained in the schema (and
            // Tenant.Name likewise), so exactly one row is expected to match here — but FirstOrDefaultAsync
            // without an ORDER BY has no contractual guarantee of which row SQL Server returns if that
            // invariant is ever violated (e.g. a schema/constraint not yet deployed to an environment).
            // Ordering makes the choice deterministic and favors the most recently created row.
            var integration = await _db.DxpIntegrations
                .AsNoTracking()
                .Where(x => x.Tenant.Name == _tenantContext.TenantName && x.IsActive && !x.IsDeleted)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (integration == null)
                return;

            var (tenantAgentId, publishedTenantAgentId) = DxpAgentIdentityResolver.ResolveIds(notification.RunnableAgent);
            var shareId = await DxpAgentIdentityResolver.ResolveShareIdAsync(_db, tenantAgentId, publishedTenantAgentId, cancellationToken);

            var context = new DxpChatbotEventContext(
                integration.DxpTenantName,
                integration.Domain,
                integration.ApiKey,
                integration.TenantId,
                _tenantContext.TenantName,
                notification.SessionId,
                _chatContext.SubjectId,
                tenantAgentId,
                publishedTenantAgentId,
                shareId,
                _chatContext.IsTestChat);

            await _dxpChatbotAnalyticsService.ReportConversationStartedAsync(context, notification.ConversationDate);
        }
    }
}
