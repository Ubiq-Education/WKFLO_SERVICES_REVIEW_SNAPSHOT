using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.ListTenantTools
{
    public class ListTenantToolsQueryHandler : IRequestHandler<ListTenantToolsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public ListTenantToolsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(ListTenantToolsQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.TenantTools.AsNoTracking()
                .Include(m => m.PublishedTenantTool)
                .ThenInclude(m => m.MarketplaceTool)
                .Where(m => m.Tenant.Name == _tenantContext.TenantName)
                .ToListAsync(cancellationToken);

            var entityIds = result.Select(t => t.Id).ToList();
            var allEntityTags = await _db.EntityTags.AsNoTracking()
                .Include(m => m.Tag)
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.Tool && entityIds.Contains(m.EntityId))
                .ToListAsync(cancellationToken);

            var mappedResults = _mapper.Map<List<TenantToolGetResult>>(result);
            foreach (var r in mappedResults)
            {
                r.Tags = _mapper.Map<List<TagGetResult>>(allEntityTags.Where(et => et.EntityId == r.Id).Select(et => et.Tag).ToList());
            }

            return new SuccessResult<List<TenantToolGetResult>>(mappedResults);
        }
    }
}
