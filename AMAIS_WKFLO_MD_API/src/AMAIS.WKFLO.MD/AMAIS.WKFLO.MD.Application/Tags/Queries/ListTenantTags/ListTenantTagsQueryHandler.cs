using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tags.Queries.ListTenantTags
{
    public class ListTenantTagsQueryHandler : IRequestHandler<ListTenantTagsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public ListTenantTagsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(ListTenantTagsQuery query, CancellationToken cancellationToken)
        {
            var tags = await _db.Tags
                .AsNoTracking()
                .Where(m => m.Tenant.Name == _tenantContext.TenantName)
                .ToListAsync(cancellationToken);

            return new SuccessResult<List<TagGetResult>>(_mapper.Map<List<TagGetResult>>(tags));
        }
    }
}
