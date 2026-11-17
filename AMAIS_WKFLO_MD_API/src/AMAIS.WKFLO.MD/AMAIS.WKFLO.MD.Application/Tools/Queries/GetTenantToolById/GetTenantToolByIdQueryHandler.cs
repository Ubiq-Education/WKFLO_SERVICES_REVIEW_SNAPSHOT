using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.GetTenantToolById
{
    public class GetTenantToolByIdQueryHandler : IRequestHandler<GetTenantToolByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public GetTenantToolByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantToolByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.TenantTools.AsNoTracking()
                .Include(m => m.PublishedTenantTool)
                .ThenInclude(m => m.MarketplaceTool)
                .Where(m => m.Id == query.Id && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
                return new NotFoundResult();

            var mapped = _mapper.Map<TenantToolGetResult>(result);

            var entityTags = await _db.EntityTags.AsNoTracking()
                .Include(m => m.Tag)
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.Tool && m.EntityId == result.Id)
                .ToListAsync(cancellationToken);

            mapped.Tags = _mapper.Map<List<TagGetResult>>(entityTags.Select(et => et.Tag).ToList());

            return new SuccessResult<TenantToolGetResult>(mapped);
        }
    }
}
