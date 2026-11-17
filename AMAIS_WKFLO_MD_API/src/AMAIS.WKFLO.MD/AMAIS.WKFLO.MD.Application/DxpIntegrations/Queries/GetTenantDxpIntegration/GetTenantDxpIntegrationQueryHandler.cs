using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.DxpIntegrations.Queries.GetTenantDxpIntegration
{
    public class GetTenantDxpIntegrationQueryHandler : IRequestHandler<GetTenantDxpIntegrationQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public GetTenantDxpIntegrationQueryHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantDxpIntegrationQuery query, CancellationToken cancellationToken)
        {
            // OrderByDescending(Id): DxpIntegration.TenantId is uniquely constrained in the schema (and
            // Tenant.Name likewise), so exactly one row is expected to match here — but FirstOrDefaultAsync
            // without an ORDER BY has no contractual guarantee of which row SQL Server returns if that
            // invariant is ever violated. Ordering makes the choice deterministic (most recently created row),
            // consistent with the DXP analytics event handlers that resolve this same row (AI API/Worker).
            var integration = await _db.DxpIntegrations
                .AsNoTracking()
                .Where(x => x.Tenant.Name == _tenantContext.TenantName && !x.IsDeleted)
                .OrderByDescending(x => x.Id)
                .Select(x => new DxpIntegrationGetResult
                {
                    Id = x.Id,
                    DxpTenantName = x.DxpTenantName,
                    Domain = x.Domain,
                    ApiKey = x.ApiKey,
                    IsActive = x.IsActive,
                    CreatedOn = x.CreatedOn,
                    LastUpdatedOn = x.LastUpdatedOn
                })
                .FirstOrDefaultAsync(cancellationToken);

            return integration == null ? new NotFoundResult() : new SuccessResult<DxpIntegrationGetResult>(integration);
        }
    }
}
