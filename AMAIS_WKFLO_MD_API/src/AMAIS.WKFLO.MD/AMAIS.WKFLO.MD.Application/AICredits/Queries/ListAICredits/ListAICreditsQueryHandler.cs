using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AICredits.Queries.ListAICredits
{
    public class ListAICreditsQueryHandler : IRequestHandler<ListAICreditsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public ListAICreditsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(ListAICreditsQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.AICredits.AsNoTracking()
                .ToListAsync(cancellationToken);

            return new SuccessResult<List<AICreditGetResult>>(_mapper.Map<List<AICreditGetResult>>(result));
        }
    }
}
