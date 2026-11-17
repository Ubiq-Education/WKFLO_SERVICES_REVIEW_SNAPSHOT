using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListMarketplaceAgents
{
    public class ListMarketplaceAgentsQueryValidator : BaseMasterDataValidator<ListMarketplaceAgentsQuery>
    {
        // Deliberately no ThereIsTenantInDataContext/BeExistingTenant rules here, unlike most other
        // MD API queries — Marketplace agents are a global/cross-tenant catalog and this endpoint has
        // never required a tenant context (see ListMarketplaceAgentsQueryHandler and MarketplaceAgentList's
        // "Marketplace Area" tag, which is excluded from the tenant-header OpenAPI transformer per
        // CLAUDE.md). Don't add tenant-scoping here.
        public ListMarketplaceAgentsQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m.Tag)
                .MaximumLength(200)
                .WithMessage("Tag must be 200 characters or fewer.");
        }
    }
}
