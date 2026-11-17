using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Validators;

namespace AMAIS.WKFLO.Common.AI.Application.Common.Validators
{
    public class BaseAIValidator<T> : BaseValidator<T>
    {
        protected readonly IMasterDataDbContext _db;
        protected readonly TenantContext _tenantContext;

        public BaseAIValidator(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        protected bool ThereIsTenantInDataContext(T _)
        {
            return !string.IsNullOrWhiteSpace(_tenantContext.TenantName);
        }

        protected bool BeExistingTenant(T _)
        {
            return _db
                .Tenants
                .Where(m => m.Name == _tenantContext.TenantName)
                .Any();
        }

        protected bool BeExistingTenantAgent(IRequestId<int> request)
        {
            return _db
                .TenantAgents
                .Where(m => m.Id == request.Id && m.Tenant.Name == _tenantContext.TenantName)
                .Any();
        }

        protected bool BeExistingAgent(IRequestId<int> request)
        {
            return _db
                .Agents
                .Where(m => m.Id == request.Id && m.Tenant.Name == _tenantContext.TenantName)
                .Any();
        }

        protected bool BeExistingRunnableAgent(IRequestId<int> request)
        {
            return BeExistingRunnableAgent(request.Id);
        }

        /// <summary>
        /// Overload of <see cref="BeExistingRunnableAgent(IRequestId{int})"/> for callers that need to
        /// validate an agent id nested inside a larger request (e.g. each step of a
        /// <c>TriggerMultiAgentOrchestratorCommand</c>) rather than the request's own top-level id.
        /// </summary>
        protected bool BeExistingRunnableAgent(int agentId)
        {
            return _db
                .TenantAgents
                .Where(m => m.Id == agentId && m.Tenant.Name == _tenantContext.TenantName && m.IsActive)
                .Any()
                ||
                _db
                .PublishedTenantAgents
                .Where(m => m.Id == agentId && m.Tenant.Name == _tenantContext.TenantName && m.IsActive)
                .Any();
        }

        protected bool BeExistingTenantAgent(IRequestShareId<string> request)
        {
            return _db
                .AgentChatSettings
                .Where(m => m.ShareId == request.ShareId && m.Agent.Tenant.Name == _tenantContext.TenantName)
                .Any();
        }

        protected bool BeExistingTenantTool(IRequestId<int> request)
        {
            return _db
                .TenantTools
                .Where(m => m.Id == request.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .Any();
        }

        protected bool BeExistingMarketplaceAgent(IRequestId<int> request)
        {
            return _db
                .MarketplaceAgents
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
        }

        protected bool BeExistingMarketplaceAgentAndBelongToTenant(IRequestId<int> request)
        {
            return _db
                .MarketplaceAgents
                .Where(m => m.Id == request.Id && m.Tenant.Name == _tenantContext.TenantName && !m.IsDeleted)
                .Any();
        }

        protected bool BeExistingMarketplaceTool(IRequestId<int> request)
        {
            return _db
                .MarketplaceTools
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
        }

        protected bool BeExistingMarketplaceToolAndBelongToTenant(IRequestId<int> request)
        {
            return _db
                .MarketplaceTools
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
        }

        protected bool BeExistingAIModel(IRequestId<int> request)
        {
            return _db
                .AIModels
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
        }

        protected bool BeExistingSecret(IRequestId<int> request)
        {
            return _db
                .Secrets
                .Where(m => m.Id == request.Id && !m.IsDeleted)
                .Any();
        }
    }
}
