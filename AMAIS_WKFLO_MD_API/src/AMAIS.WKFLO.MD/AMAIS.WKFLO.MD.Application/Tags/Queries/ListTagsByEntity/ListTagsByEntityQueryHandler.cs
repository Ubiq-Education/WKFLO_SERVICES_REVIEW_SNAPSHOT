using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tags.Queries.ListTagsByEntity
{
    public class ListTagsByEntityQueryHandler : IRequestHandler<ListTagsByEntityQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public ListTagsByEntityQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(ListTagsByEntityQuery query, CancellationToken cancellationToken)
        {
            var tags = await _db.EntityTags
                .AsNoTracking()
                .Where(m => m.EntityType == query.EntityType && m.EntityId == query.EntityId && m.Tag.Tenant.Name == _tenantContext.TenantName)
                .Select(m => m.Tag)
                .ToListAsync(cancellationToken);

            return new SuccessResult<List<TagGetResult>>(_mapper.Map<List<TagGetResult>>(tags));
        }
    }
}
