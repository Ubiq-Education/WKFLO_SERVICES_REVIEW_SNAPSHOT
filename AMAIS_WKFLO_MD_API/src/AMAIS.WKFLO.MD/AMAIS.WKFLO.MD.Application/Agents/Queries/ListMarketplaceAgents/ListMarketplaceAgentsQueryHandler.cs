using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListMarketplaceAgents
{
    public class ListMarketplaceAgentsQueryHandler : IRequestHandler<ListMarketplaceAgentsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public ListMarketplaceAgentsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(ListMarketplaceAgentsQuery query, CancellationToken cancellationToken)
        {
            var agentsQuery = _db.MarketplaceAgents.AsNoTracking()
                .Include(m => m.AIModel)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Tag))
            {
                // Marketplace agents are a global catalog (no tenant scoping on this endpoint), so the tag
                // is matched by name across every tenant's tags rather than the caller's own — see the
                // doc comment on ListMarketplaceAgentsQuery.Tag. EntityTag rows are never cleaned up on
                // delete (see ListEntitiesByTagQueryHandler's comment on the same pattern), but that's
                // harmless here: intersecting with the live (non-soft-deleted) MarketplaceAgents queryable
                // below already excludes any stale tag rows pointing at a deleted/nonexistent agent.
                var taggedAgentIds = await _db.EntityTags
                    .AsNoTracking()
                    .Where(t => t.EntityType == CommonConsts.TaggableEntityTypes.Agent && t.Tag.Name == query.Tag)
                    .Select(t => t.EntityId)
                    .ToListAsync(cancellationToken);

                agentsQuery = agentsQuery.Where(m => taggedAgentIds.Contains(m.Id));
            }

            var result = await agentsQuery.ToListAsync(cancellationToken);

            if (result == null)
            {
                return new NotFoundResult();
            }

            var mapped = _mapper.Map<List<MarketplaceAgentGetResult>>(result);

            // Live installation counts: MarketplaceAgent.Installations is no longer a stored column,
            // it's computed as the count of non-deleted TenantAgents installed from each marketplace
            // agent. Batched into one grouped query rather than N+1 per-agent counts.
            var agentIdsForCount = result.Select(m => m.Id).ToList();
            var installationCounts = await _db.TenantAgents
                .AsNoTracking()
                .Where(ta => ta.InstalledFromMarketplaceAgentId != null && agentIdsForCount.Contains(ta.InstalledFromMarketplaceAgentId.Value) && !ta.IsDeleted)
                .GroupBy(ta => ta.InstalledFromMarketplaceAgentId.Value)
                .Select(g => new { MarketplaceAgentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.MarketplaceAgentId, g => g.Count, cancellationToken);

            foreach (var agent in mapped)
            {
                agent.Installations = installationCounts.TryGetValue(agent.Id, out var count) ? count : 0;
            }

            // Live star ratings: MarketplaceAgent.Stars is no longer a stored column, it's computed as
            // the rounded average of all non-deleted UserStar ratings for each marketplace agent.
            // Batched into one grouped query rather than N+1 per-agent averages; agents with no ratings
            // yet default to 0 (avoids a divide-by-zero/null average).
            var starAverages = await _db.UserStars
                .AsNoTracking()
                .Where(us => us.Type == nameof(MarketplaceAgent) && agentIdsForCount.Contains(us.TypeId) && !us.IsDeleted)
                .GroupBy(us => us.TypeId)
                .Select(g => new { MarketplaceAgentId = g.Key, Average = g.Average(us => (double)us.Value) })
                .ToDictionaryAsync(g => g.MarketplaceAgentId, g => g.Average, cancellationToken);

            foreach (var agent in mapped)
            {
                agent.Stars = starAverages.TryGetValue(agent.Id, out var average)
                    ? (int)Math.Round(average, MidpointRounding.AwayFromZero)
                    : 0;
            }

            // Attach each agent's current tags directly in the list response, resolved cross-tenant
            // (no _tenantContext filter) just like the Tag filter above - Marketplace agents are a
            // global catalog, so a tenant-scoped lookup here (like ListTagsByEntityQueryHandler, used
            // for tenant-owned entities) would wrongly return an empty tag list whenever the tag was
            // assigned under a tenant other than whichever one the caller currently has selected.
            var agentIds = result.Select(m => m.Id).ToList();
            var entityTags = await _db.EntityTags
                .AsNoTracking()
                .Where(t => t.EntityType == CommonConsts.TaggableEntityTypes.Agent && agentIds.Contains(t.EntityId))
                .Select(t => new { t.EntityId, t.Tag })
                .ToListAsync(cancellationToken);

            var tagsByAgentId = entityTags
                .GroupBy(t => t.EntityId)
                .ToDictionary(g => g.Key, g => _mapper.Map<List<TagGetResult>>(g.Select(x => x.Tag).ToList()));

            foreach (var agent in mapped)
            {
                agent.Tags = tagsByAgentId.TryGetValue(agent.Id, out var tags) ? tags : new List<TagGetResult>();
            }

            return new SuccessResult<List<MarketplaceAgentGetResult>>(mapped);
        }
    }
}
