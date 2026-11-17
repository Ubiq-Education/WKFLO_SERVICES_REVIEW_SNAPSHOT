using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AIModels.Queries.ListAIModels
{
    public class ListAIModelsQueryHandler : IRequestHandler<ListAIModelsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public ListAIModelsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(ListAIModelsQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.AIModels.AsNoTracking()
                .ToListAsync(cancellationToken);

            var entityIds = result.Select(m => m.Id).ToList();
            var allEntityTags = await _db.EntityTags.AsNoTracking()
                .Include(m => m.Tag)
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.AIModel && entityIds.Contains(m.EntityId))
                .ToListAsync(cancellationToken);

            var mappedResults = _mapper.Map<List<AIModelGetResult>>(result);
            foreach (var r in mappedResults)
            {
                r.Tags = _mapper.Map<List<TagGetResult>>(allEntityTags.Where(et => et.EntityId == r.Id).Select(et => et.Tag).ToList());
            }

            return new SuccessResult<List<AIModelGetResult>>(mappedResults);
        }
    }
}
