using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tenants.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenants
{
    public class ListTenantsQueryHandler : IRequestHandler<ListTenantsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public ListTenantsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(ListTenantsQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.Tenants.AsNoTracking()
                .ToListAsync(cancellationToken);

            return result == null ? new NotFoundResult() : (Result)new SuccessResult<List<TenantGetResult>>(_mapper.Map<List<TenantGetResult>>(result));
        }
    }
}
