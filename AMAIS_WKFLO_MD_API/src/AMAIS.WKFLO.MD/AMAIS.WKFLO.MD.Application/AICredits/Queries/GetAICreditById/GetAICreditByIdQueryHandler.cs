using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditById
{
    public class GetAICreditByIdQueryHandler : IRequestHandler<GetAICreditByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetAICreditByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetAICreditByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.AICredits.AsNoTracking()
                .Where(m => m.Id == query.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
                return new NotFoundResult();

            return new SuccessResult<AICreditGetResult>(_mapper.Map<AICreditGetResult>(result));
        }
    }
}
