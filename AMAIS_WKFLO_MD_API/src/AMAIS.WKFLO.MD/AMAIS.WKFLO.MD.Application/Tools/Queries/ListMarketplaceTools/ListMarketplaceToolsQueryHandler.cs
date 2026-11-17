using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.ListMarketplaceTools
{
    public class ListMarketplaceToolsQueryHandler : IRequestHandler<ListMarketplaceToolsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public ListMarketplaceToolsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(ListMarketplaceToolsQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.MarketplaceTools.AsNoTracking()
                .ToListAsync(cancellationToken);

            if (result == null)
                return new NotFoundResult();

            var mapped = _mapper.Map<List<MarketplaceToolGetResult>>(result);

            // Live installation counts: MarketplaceTool.Installations is no longer a stored column,
            // it's computed as the count of non-deleted TenantTools installed from each marketplace
            // tool. Batched into one grouped query rather than N+1 per-tool counts.
            var toolIdsForCount = result.Select(m => m.Id).ToList();
            var installationCounts = await _db.TenantTools
                .AsNoTracking()
                .Where(tt => tt.InstalledFromMarketplaceToolId != null && toolIdsForCount.Contains(tt.InstalledFromMarketplaceToolId.Value) && !tt.IsDeleted)
                .GroupBy(tt => tt.InstalledFromMarketplaceToolId.Value)
                .Select(g => new { MarketplaceToolId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.MarketplaceToolId, g => g.Count, cancellationToken);

            foreach (var tool in mapped)
            {
                tool.Installations = installationCounts.TryGetValue(tool.Id, out var count) ? count : 0;
            }

            // Live star ratings: MarketplaceTool.Stars is no longer a stored column, it's computed as
            // the rounded average of all non-deleted UserStar ratings for each marketplace tool.
            // Batched into one grouped query rather than N+1 per-tool averages; tools with no ratings
            // yet default to 0 (avoids a divide-by-zero/null average).
            var starAverages = await _db.UserStars
                .AsNoTracking()
                .Where(us => us.Type == nameof(MarketplaceTool) && toolIdsForCount.Contains(us.TypeId) && !us.IsDeleted)
                .GroupBy(us => us.TypeId)
                .Select(g => new { MarketplaceToolId = g.Key, Average = g.Average(us => (double)us.Value) })
                .ToDictionaryAsync(g => g.MarketplaceToolId, g => g.Average, cancellationToken);

            foreach (var tool in mapped)
            {
                tool.Stars = starAverages.TryGetValue(tool.Id, out var average)
                    ? (int)Math.Round(average, MidpointRounding.AwayFromZero)
                    : 0;
            }

            return new SuccessResult<List<MarketplaceToolGetResult>>(mapped);
        }
    }
}
