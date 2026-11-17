using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.ContactFormSubmitted
{
    public class ReportContactFormSubmittedDomainEventHandler : INotificationHandler<ContactFormSubmittedDomainEvent>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IDxpContactFormService _dxpContactFormService;

        public ReportContactFormSubmittedDomainEventHandler(
            IMasterDataDbContext db,
            IDxpContactFormService dxpContactFormService)
        {
            _db = db;
            _dxpContactFormService = dxpContactFormService;
        }

        public async ValueTask Handle(ContactFormSubmittedDomainEvent notification, CancellationToken cancellationToken)
        {
            // OrderByDescending(Id): DxpIntegration.TenantId is uniquely constrained in the schema (and
            // Tenant.Name likewise), so exactly one row is expected to match here — but FirstOrDefaultAsync
            // without an ORDER BY has no contractual guarantee of which row SQL Server returns if that
            // invariant is ever violated. Ordering makes the choice deterministic (most recently created row).
            var integration = await _db.DxpIntegrations
                .AsNoTracking()
                .Where(x => x.Tenant.Name == notification.TenantName && x.IsActive && !x.IsDeleted)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (integration == null)
                return;

            // notification carries only ONE of {AgentId, ShareId} depending on which submission path fired
            // (tenant-scoped SubmitContactFormCommandHandler vs public/share-based
            // SubmitPublicAgentContactFormCommandHandler) — but every other chatbot analytics event resolves
            // and sends all 3 identifiers together (see ReportConversationResolutionAnalyzedDomainEventHandler),
            // and CMS's Analytics dashboard only ever queries by TenantAgentId. A contact form submitted via
            // the public/share path must not become invisible to it just because ShareId was the only id known
            // at submission time — resolve whichever ones are still missing from whichever one is known.
            var (tenantAgentId, publishedTenantAgentId) = notification.AgentId.HasValue
                ? await DxpAgentIdentityResolver.ResolveIdsAsync(_db, notification.AgentId.Value, cancellationToken)
                : await DxpAgentIdentityResolver.ResolveIdsFromShareIdAsync(_db, notification.ShareId, cancellationToken);

            var shareId = !string.IsNullOrWhiteSpace(notification.ShareId)
                ? notification.ShareId
                : await DxpAgentIdentityResolver.ResolveShareIdAsync(_db, tenantAgentId, publishedTenantAgentId, cancellationToken);

            await _dxpContactFormService.ReportContactFormSubmissionAsync(
                integration.DxpTenantName,
                integration.Domain,
                integration.ApiKey,
                notification.SubjectId,
                notification.SessionId,
                notification.FirstName,
                notification.LastName,
                notification.Email,
                tenantAgentId,
                publishedTenantAgentId,
                shareId);
        }
    }
}
