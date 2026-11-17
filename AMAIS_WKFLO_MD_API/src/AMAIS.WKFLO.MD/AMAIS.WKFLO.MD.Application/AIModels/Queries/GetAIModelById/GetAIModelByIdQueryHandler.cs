using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AIModels.Queries.GetAIModelById
{
    public class GetAIModelByIdQueryHandler : IRequestHandler<GetAIModelByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetAIModelByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetAIModelByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.AIModels.AsNoTracking()
                .Where(m => m.Id == query.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
                return new NotFoundResult();

            var mapped = _mapper.Map<AIModelGetResult>(result);

            var entityTags = await _db.EntityTags.AsNoTracking()
                .Include(m => m.Tag)
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.AIModel && m.EntityId == result.Id)
                .ToListAsync(cancellationToken);

            mapped.Tags = _mapper.Map<List<TagGetResult>>(entityTags.Select(et => et.Tag).ToList());

            return new SuccessResult<AIModelGetResult>(mapped);
        }
    }
}
