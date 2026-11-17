using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.GetMarketplaceToolById
{
    public class GetMarketplaceToolByIdQueryHandler : IRequestHandler<GetMarketplaceToolByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetMarketplaceToolByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetMarketplaceToolByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.MarketplaceTools.AsNoTracking()
                .Where(m => m.Id == query.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
                return new NotFoundResult();

            var mapped = _mapper.Map<MarketplaceToolGetResult>(result);

            // Live installation count: see ListMarketplaceToolsQueryHandler's comment for why this
            // isn't a stored column anymore.
            mapped.Installations = await _db.TenantTools
                .AsNoTracking()
                .CountAsync(tt => tt.InstalledFromMarketplaceToolId == result.Id && !tt.IsDeleted, cancellationToken);

            // Live star rating: MarketplaceTool.Stars is no longer a stored column, it's computed as
            // the rounded average of all non-deleted UserStar ratings for this marketplace tool - see
            // ListMarketplaceToolsQueryHandler's comment. Defaults to 0 when there are no ratings yet
            // (avoids a divide-by-zero/null average).
            var starAverage = await _db.UserStars
                .AsNoTracking()
                .Where(us => us.Type == nameof(MarketplaceTool) && us.TypeId == result.Id && !us.IsDeleted)
                .Select(us => (double?)us.Value)
                .AverageAsync(cancellationToken);

            mapped.Stars = starAverage.HasValue ? (int)Math.Round(starAverage.Value, MidpointRounding.AwayFromZero) : 0;

            return new SuccessResult<MarketplaceToolGetResult>(mapped);
        }
    }
}
