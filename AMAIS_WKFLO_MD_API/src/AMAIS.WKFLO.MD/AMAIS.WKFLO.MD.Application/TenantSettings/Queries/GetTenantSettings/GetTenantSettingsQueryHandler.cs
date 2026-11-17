using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantSettings.Models;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.TenantSettings.Queries.GetTenantSettings
{
    public class GetTenantSettingsQueryHandler : IRequestHandler<GetTenantSettingsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public GetTenantSettingsQueryHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantSettingsQuery query, CancellationToken cancellationToken)
        {
            var tenantId = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Name == _tenantContext.TenantName && !t.IsDeleted)
                .Select(t => (int?)t.Id)
                .Cacheable()
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantId is null)
                return new NotFoundResult();

            var setting = await _db.TenantSettings
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId.Value && !s.IsDeleted)
                .Cacheable()
                .FirstOrDefaultAsync(cancellationToken);

            // Always 200: a tenant that has never saved its settings gets TenantSettingDefaults.
            return new SuccessResult<TenantSettingsResult>(TenantSettingsResult.Create(setting));
        }
    }
}
