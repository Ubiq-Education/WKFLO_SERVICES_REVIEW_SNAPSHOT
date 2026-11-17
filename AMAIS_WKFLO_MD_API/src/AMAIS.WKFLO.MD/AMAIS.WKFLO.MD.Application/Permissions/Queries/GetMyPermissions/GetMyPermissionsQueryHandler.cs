using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Permissions.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Permissions.Queries.GetMyPermissions
{
    public class GetMyPermissionsQueryHandler : IRequestHandler<GetMyPermissionsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;

        public GetMyPermissionsQueryHandler(IMasterDataDbContext db, UserContext userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(GetMyPermissionsQuery query, CancellationToken cancellationToken)
        {
            var email = PermissionHelper.NormaliseEmail(_userContext.Email);
            var permissions = new MyPermissionsResult();

            if (email.Length == 0)
                return new SuccessResult<MyPermissionsResult>(permissions);

            permissions.IsSuperPowerUser = await PermissionHelper.IsSuperPowerUserAsync(_db, email, cancellationToken);
            if (permissions.IsSuperPowerUser)
                return new SuccessResult<MyPermissionsResult>(permissions);

            var guids = await _db.TenantAccesses
                .AsNoTracking()
                .Where(a => a.Email == email && !a.IsDeleted && !a.Tenant.IsDeleted)
                .Select(a => a.Tenant.Guid)
                .ToListAsync(cancellationToken);

            permissions.AccessibleTenantGuids = guids.Distinct().Select(g => g.ToString()).ToList();
            return new SuccessResult<MyPermissionsResult>(permissions);
        }
    }
}
