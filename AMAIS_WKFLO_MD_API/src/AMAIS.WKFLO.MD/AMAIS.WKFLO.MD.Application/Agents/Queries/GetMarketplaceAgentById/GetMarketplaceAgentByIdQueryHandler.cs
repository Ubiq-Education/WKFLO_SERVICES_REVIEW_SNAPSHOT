using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentById
{
    public class GetMarketplaceAgentByIdQueryHandler : IRequestHandler<GetMarketplaceAgentByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetMarketplaceAgentByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetMarketplaceAgentByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.MarketplaceAgents.AsNoTracking()
                .Include(m => m.AIModel)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == query.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
                return new NotFoundResult();

            var mapped = _mapper.Map<MarketplaceAgentGetResult>(result);

            // Live installation count: see ListMarketplaceAgentsQueryHandler's comment for why this
            // isn't a stored column anymore.
            mapped.Installations = await _db.TenantAgents
                .AsNoTracking()
                .CountAsync(ta => ta.InstalledFromMarketplaceAgentId == result.Id && !ta.IsDeleted, cancellationToken);

            // Live star rating: MarketplaceAgent.Stars is no longer a stored column, it's computed as
            // the rounded average of all non-deleted UserStar ratings for this marketplace agent -
            // see ListMarketplaceAgentsQueryHandler's comment. Defaults to 0 when there are no ratings
            // yet (avoids a divide-by-zero/null average).
            var starAverage = await _db.UserStars
                .AsNoTracking()
                .Where(us => us.Type == nameof(MarketplaceAgent) && us.TypeId == result.Id && !us.IsDeleted)
                .Select(us => (double?)us.Value)
                .AverageAsync(cancellationToken);

            mapped.Stars = starAverage.HasValue ? (int)Math.Round(starAverage.Value, MidpointRounding.AwayFromZero) : 0;

            return new SuccessResult<MarketplaceAgentGetResult>(mapped);
        }
    }
}
