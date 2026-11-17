using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Permissions;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.RemoveSuperPowerUser
{
    public class RemoveSuperPowerUserCommandHandler : IRequestHandler<RemoveSuperPowerUserCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;

        public RemoveSuperPowerUserCommandHandler(IMasterDataDbContext db, UserContext userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(RemoveSuperPowerUserCommand command, CancellationToken cancellationToken)
        {
            var callerEmail = PermissionHelper.NormaliseEmail(_userContext.Email);
            if (!await PermissionHelper.IsSuperPowerUserAsync(_db, callerEmail, cancellationToken))
                return new ForbiddenResult(nameof(UserContext.Email), ErrorConst.SuperPowerUserRequired);

            var row = await _db.SuperPowerUsers
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == command.Id && !s.IsDeleted, cancellationToken);

            if (row is null)
                return new NotFoundResult();

            if (string.Equals(row.Email, callerEmail, StringComparison.OrdinalIgnoreCase))
                return new InvalidResult(nameof(command.Id), ErrorConst.CannotRemoveOwnSuperPowerUser);

            // Pre-check: the caller is an active super power user (checked above) and is not the row being
            // removed, so another active row normally remains.
            var otherActive = await _db.SuperPowerUsers
                .AsNoTracking()
                .AnyAsync(s => s.Id != row.Id && !s.IsDeleted, cancellationToken);

            if (!otherActive)
                return new InvalidResult(nameof(command.Id), ErrorConst.CannotRemoveLastSuperPowerUser);

            row.IsDeleted = true;
            await _db.SaveChangesAsync(cancellationToken);

            // The pre-check is not atomic (no transactions here): two super power users removing each other at
            // the same time both pass it, because each still sees the other as active, and both saves commit.
            // So re-check after our save. Whichever request commits last sees zero active rows and reverts its
            // own removal, which guarantees at least one active super power user always remains.
            var anyActive = await _db.SuperPowerUsers
                .AsNoTracking()
                .AnyAsync(s => !s.IsDeleted, cancellationToken);

            if (!anyActive)
            {
                row.IsDeleted = false;
                await _db.SaveChangesAsync(cancellationToken);
                return new InvalidResult(nameof(command.Id), ErrorConst.CannotRemoveLastSuperPowerUser);
            }

            return new SuccessResult<SuperPowerUserResult>(SuperPowerUserResult.From(row));
        }
    }
}
