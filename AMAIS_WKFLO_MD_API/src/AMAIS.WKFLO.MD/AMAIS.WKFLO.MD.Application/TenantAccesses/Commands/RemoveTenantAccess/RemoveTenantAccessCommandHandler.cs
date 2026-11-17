using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Permissions;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.RemoveTenantAccess
{
    public class RemoveTenantAccessCommandHandler : IRequestHandler<RemoveTenantAccessCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;

        public RemoveTenantAccessCommandHandler(IMasterDataDbContext db, UserContext userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(RemoveTenantAccessCommand command, CancellationToken cancellationToken)
        {
            var callerEmail = PermissionHelper.NormaliseEmail(_userContext.Email);
            if (!await PermissionHelper.IsSuperPowerUserAsync(_db, callerEmail, cancellationToken))
                return new ForbiddenResult(nameof(UserContext.Email), ErrorConst.SuperPowerUserRequired);

            var row = await _db.TenantAccesses
                .AsTracking()
                .Include(a => a.Tenant)
                .FirstOrDefaultAsync(a => a.Id == command.Id && !a.IsDeleted, cancellationToken);

            if (row is null)
                return new NotFoundResult();

            row.IsDeleted = true;
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantAccessResult>(TenantAccessResult.From(row, row.Tenant));
        }
    }
}
