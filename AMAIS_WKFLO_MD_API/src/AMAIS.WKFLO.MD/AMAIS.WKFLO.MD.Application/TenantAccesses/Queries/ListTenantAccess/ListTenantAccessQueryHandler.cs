using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Permissions;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.TenantAccesses.Queries.ListTenantAccess
{
    public class ListTenantAccessQueryHandler : IRequestHandler<ListTenantAccessQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;

        public ListTenantAccessQueryHandler(IMasterDataDbContext db, UserContext userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListTenantAccessQuery query, CancellationToken cancellationToken)
        {
            var callerEmail = PermissionHelper.NormaliseEmail(_userContext.Email);
            if (!await PermissionHelper.IsSuperPowerUserAsync(_db, callerEmail, cancellationToken))
                return new ForbiddenResult(nameof(UserContext.Email), ErrorConst.SuperPowerUserRequired);

            var rows = await _db.TenantAccesses
                .AsNoTracking()
                .Where(a => !a.IsDeleted && !a.Tenant.IsDeleted)
                .OrderBy(a => a.Tenant.Name)
                .ThenBy(a => a.Email)
                .Select(a => new { Access = a, a.Tenant })
                .ToListAsync(cancellationToken);

            return new SuccessResult<List<TenantAccessResult>>(
                rows.Select(r => TenantAccessResult.From(r.Access, r.Tenant)).ToList());
        }
    }
}
