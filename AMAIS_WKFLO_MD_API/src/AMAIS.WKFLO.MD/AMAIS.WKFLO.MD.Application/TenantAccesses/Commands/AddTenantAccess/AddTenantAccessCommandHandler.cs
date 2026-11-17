using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Permissions;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.AddTenantAccess
{
    public class AddTenantAccessCommandHandler : IRequestHandler<AddTenantAccessCommand, Result>
    {
        private const int MaxSaveAttempts = 2;

        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;

        public AddTenantAccessCommandHandler(IMasterDataDbContext db, UserContext userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(AddTenantAccessCommand command, CancellationToken cancellationToken)
        {
            var callerEmail = PermissionHelper.NormaliseEmail(_userContext.Email);
            if (!await PermissionHelper.IsSuperPowerUserAsync(_db, callerEmail, cancellationToken))
                return new ForbiddenResult(nameof(UserContext.Email), ErrorConst.SuperPowerUserRequired);

            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Guid == command.TenantGuid && !t.IsDeleted, cancellationToken);

            if (tenant is null)
                return new InvalidResult(nameof(command.TenantGuid), ErrorConst.TenantAccessTenantNotFound);

            var email = PermissionHelper.NormaliseEmail(command.Email);

            for (var attempt = 1; ; attempt++)
            {
                // Tracked and unfiltered on IsDeleted: the unique index covers soft-deleted rows, so a removed
                // grant is reactivated instead of inserting a duplicate.
                var existing = await _db.TenantAccesses
                    .AsTracking()
                    .FirstOrDefaultAsync(a => a.TenantId == tenant.Id && a.Email == email, cancellationToken);

                var inserted = false;
                if (existing is null)
                {
                    existing = new TenantAccess { TenantId = tenant.Id, Email = email, CreatedBy = callerEmail };
                    _db.TenantAccesses.Add(existing);
                    inserted = true;
                }
                else if (existing.IsDeleted)
                {
                    existing.IsDeleted = false;
                    existing.CreatedBy = callerEmail;
                }
                else
                {
                    return new SuccessResult<TenantAccessResult>(TenantAccessResult.From(existing, tenant));
                }

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return new SuccessResult<TenantAccessResult>(TenantAccessResult.From(existing, tenant));
                }
                catch (DbUpdateException) when (inserted && attempt < MaxSaveAttempts)
                {
                    // The read-then-insert above is not atomic: a concurrent add of the same (tenant, email) can
                    // insert its row between our read and our save, and the unique (TenantId, Email) index
                    // rejects ours. Discard everything this failed save left in the tracker (our Added row and
                    // the AuditLogEntry rows SaveChangesAsync appended for it), then re-read - the competing row
                    // is now visible, so the retry returns (or reactivates) it instead of inserting. Bounded to
                    // one retry: if the failure was not that race, the retry fails the same way and the
                    // exception surfaces.
                    _db.ChangeTracker.Clear();
                }
            }
        }
    }
}
