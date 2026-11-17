using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditByAIModelId
{
    public class GetAICreditByAIModelIdQueryHandler : IRequestHandler<GetAICreditByAIModelIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetAICreditByAIModelIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetAICreditByAIModelIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.AICredits.AsNoTracking()
                .Where(m => m.AIModelId == query.AIModelId)
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
                return new NotFoundResult();

            return new SuccessResult<AICreditGetResult>(_mapper.Map<AICreditGetResult>(result));
        }
    }
}
