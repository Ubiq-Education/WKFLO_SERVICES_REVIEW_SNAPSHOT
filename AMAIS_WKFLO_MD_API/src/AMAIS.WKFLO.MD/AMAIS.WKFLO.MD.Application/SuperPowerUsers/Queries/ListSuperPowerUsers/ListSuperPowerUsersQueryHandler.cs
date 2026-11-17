using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Permissions;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.SuperPowerUsers.Queries.ListSuperPowerUsers
{
    public class ListSuperPowerUsersQueryHandler : IRequestHandler<ListSuperPowerUsersQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;

        public ListSuperPowerUsersQueryHandler(IMasterDataDbContext db, UserContext userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListSuperPowerUsersQuery query, CancellationToken cancellationToken)
        {
            var callerEmail = PermissionHelper.NormaliseEmail(_userContext.Email);
            if (!await PermissionHelper.IsSuperPowerUserAsync(_db, callerEmail, cancellationToken))
                return new ForbiddenResult(nameof(UserContext.Email), ErrorConst.SuperPowerUserRequired);

            var rows = await _db.SuperPowerUsers
                .AsNoTracking()
                .Where(s => !s.IsDeleted)
                .OrderBy(s => s.Email)
                .ToListAsync(cancellationToken);

            return new SuccessResult<List<SuperPowerUserResult>>(rows.Select(SuperPowerUserResult.From).ToList());
        }
    }
}
