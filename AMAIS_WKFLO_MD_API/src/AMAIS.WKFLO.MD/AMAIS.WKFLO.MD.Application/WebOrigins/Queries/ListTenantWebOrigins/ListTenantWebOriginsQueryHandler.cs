using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Permissions;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.WebOrigins.Queries.ListTenantWebOrigins
{
    /// <summary>
    /// Lists every stored web origin of the current tenant (including soft-deleted and inactive rows) for
    /// super power users. Deliberately not cached: support needs the true stored state.
    /// </summary>
    public class ListTenantWebOriginsQueryHandler : IRequestHandler<ListTenantWebOriginsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public ListTenantWebOriginsQueryHandler(IMasterDataDbContext db, TenantContext tenantContext, UserContext userContext)
        {
            _db = db;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListTenantWebOriginsQuery query, CancellationToken cancellationToken)
        {
            var callerEmail = PermissionHelper.NormaliseEmail(_userContext.Email);
            if (!await PermissionHelper.IsSuperPowerUserAsync(_db, callerEmail, cancellationToken))
                return new ForbiddenResult(nameof(UserContext.Email), ErrorConst.SuperPowerUserRequired);

            var tenantId = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Name == _tenantContext.TenantName && !t.IsDeleted)
                .Select(t => (int?)t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantId is null)
                return new NotFoundResult();

            var rows = await _db.WebOrigins
                .AsNoTracking()
                .Where(w => w.TenantId == tenantId.Value)
                .OrderBy(w => w.WebOriginType)
                .ThenBy(w => w.Name)
                .ToListAsync(cancellationToken);

            return new SuccessResult<List<TenantWebOriginListResult>>(rows.Select(TenantWebOriginListResult.From).ToList());
        }
    }
}
