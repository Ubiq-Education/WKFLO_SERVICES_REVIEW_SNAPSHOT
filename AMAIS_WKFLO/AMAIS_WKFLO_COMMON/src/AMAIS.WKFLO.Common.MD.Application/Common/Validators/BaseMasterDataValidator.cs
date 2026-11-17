using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Validators;

namespace AMAIS.WKFLO.Common.MD.Application.Common.Validators
{
    public class BaseMasterDataValidator<T> : BaseValidator<T>
    {
        protected readonly IMasterDataDbContext _db;
        protected readonly TenantContext _tenantContext;

        public BaseMasterDataValidator(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        protected bool ThereIsTenantInDataContext(T _)
        {
            var res = !string.IsNullOrWhiteSpace(_tenantContext.TenantName);
            return res;
        }

        protected bool BeExistingTenant(T _)
        {
            var res = _db
                .Tenants
                .Where(m => m.Name == _tenantContext.TenantName)
                .Any();
            return res;
        }

        protected bool BeExistingTenantAgent(IRequestId<int> request)
        {
            var res = _db
                .TenantAgents
                .Where(m => m.Id == request.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .Any();
            return res;
        }

        protected bool BeExistingPublishedTenantAgent(IRequestId<int> request)
        {
            var res = _db
                .PublishedTenantAgents
                .Where(m => m.Id == request.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .Any();
            return res;
        }

        protected bool BeExistingTenantTool(IRequestId<int> request)
        {
            var res = _db
                .TenantTools
                .Where(m => m.Id == request.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .Any();
            return res;
        }

        /// <summary>Like <see cref="BeExistingTenantTool(IRequestId{int})"/>, but for the MCP OAuth
        /// start/status/disconnect endpoints, which are keyed by RunableTool.McpOAuthConnectionId rather
        /// than Tool.Id.</summary>
        protected bool BeExistingTenantToolByMcpOAuthConnectionId(IRequestId<Guid> request)
        {
            var res = _db
                .TenantTools
                .Where(m => m.McpOAuthConnectionId == request.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .Any();
            return res;
        }

        /// <summary>
        /// Marketplace agents are a global catalog, not tenant-scoped data (same convention as
        /// <see cref="BeExistingMarketplaceTool(IRequestId{int})"/>) — the Marketplace-scope admin
        /// operations (update/delete/upload-icon, all under
        /// <c>EndPoints/Marketplace/Agents/</c>) are AMAIS-staff "Manage Marketplace Agents" tooling
        /// that must be able to see/manage every marketplace agent across every tenant, not just
        /// whichever tenant the caller's <see cref="TenantContext"/> currently happens to be. A
        /// previous <c>BeExistingMarketplaceAgentAndBelongToTenant</c> variant of this method
        /// incorrectly filtered by <c>m.Tenant.Name == _tenantContext.TenantName</c> (the tenant
        /// that originally shared the agent, set at share-time — see
        /// <c>ShareTenantAgentCommandHandler</c>) and was used by
        /// <c>UpdateMarketplaceAgentCommandValidator</c>/<c>DeleteMarketplaceAgentCommandValidator</c>,
        /// silently 400ing any AMAIS-staff caller whose current tenant header didn't match the
        /// agent's originating tenant. Removed as a real access-control bug; both validators now
        /// call this method instead, matching <c>RateMarketplaceAgentCommandValidator</c>'s existing
        /// (correct) usage and <c>ListMarketplaceAgentsQueryHandler</c>'s explicit "no tenant scoping
        /// on this endpoint" design.
        /// </summary>
        protected bool BeExistingMarketplaceAgent(IRequestId<int> request)
        {
            var res = _db
                .MarketplaceAgents
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
            return res;
        }

        protected bool BeExistingMarketplaceTool(IRequestId<int> request)
        {
            var res = _db
                .MarketplaceTools
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
            return res;
        }

        protected bool BeExistingMarketplaceToolAndBelongToTenant(IRequestId<int> request)
        {
            var res = _db
                .MarketplaceTools
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
            return res;
        }

        protected bool BeExistingAIModel(IRequestId<int> request)
        {
            var res = _db
                .AIModels
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
            return res;
        }

        protected bool BeExistingSecret(IRequestId<int> request)
        {
            var res = _db
                .Secrets
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
            return res;
        }

        protected bool BeExistingTag(IRequestId<int> request)
        {
            var res = _db
                .Tags
                .Where(m => m.Id == request.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .Any();
            return res;
        }

        protected bool BeExistingAICredit(IRequestId<int> request)
        {
            var res = _db
                .AICredits
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
            return res;
        }
    }
}
