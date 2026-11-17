using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tags.Queries.ListEntitiesByTag
{
    public class ListEntitiesByTagQueryHandler : IRequestHandler<ListEntitiesByTagQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public ListEntitiesByTagQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(ListEntitiesByTagQuery query, CancellationToken cancellationToken)
        {
            var entityTags = await _db.EntityTags
                .AsNoTracking()
                .Where(m => m.TagId == query.Id && m.Tag.Tenant.Name == _tenantContext.TenantName)
                .ToListAsync(cancellationToken);

            // EntityTag has no FK/navigation to the tables it points at (a deliberate design — see
            // CommonConsts.TaggableEntityTypes), and it is never cleaned up on delete: any delete/manual DB
            // edit that bypasses the owning handler would leave orphaned rows regardless. So liveness is
            // enforced here, at read time, by checking whether each tagged id is still present in its real
            // table's live (non-soft-deleted) queryable set — correct no matter how/when the row died.
            //
            // EntityType comparisons must be case-insensitive: AssignTagToEntityCommandValidator validates
            // it against a HashSet<string>(StringComparer.OrdinalIgnoreCase), so a caller can legitimately
            // send e.g. "agent" lowercase, and AssignTagToEntityCommandHandler stores it verbatim with no
            // normalization. An exact `==` here would silently drop an otherwise-live, legitimately-tagged
            // entity just because of casing.
            var agentIds = entityTags.Where(t => string.Equals(t.EntityType, CommonConsts.TaggableEntityTypes.Agent, StringComparison.OrdinalIgnoreCase)).Select(t => t.EntityId).ToList();
            var toolIds = entityTags.Where(t => string.Equals(t.EntityType, CommonConsts.TaggableEntityTypes.Tool, StringComparison.OrdinalIgnoreCase)).Select(t => t.EntityId).ToList();
            var aiModelIds = entityTags.Where(t => string.Equals(t.EntityType, CommonConsts.TaggableEntityTypes.AIModel, StringComparison.OrdinalIgnoreCase)).Select(t => t.EntityId).ToList();

            // Agent, Tool, and AIModel all derive from the same Entity<int> base, so a single generic helper
            // can check "is this id still in the live (non-soft-deleted) queryable set" against any of
            // their concrete DbSets, instead of hand-writing the same Where/Select/ToListAsync per type.
            // Agent has a base DbSet<Agent> covering all its TPH subclasses (TenantAgent, PublishedTenantAgent,
            // MarketplaceAgent) in one query. Tool has no equivalent base DbSet on IMasterDataDbContext, so
            // its live ids are the union of the helper called against each of its three TPH subclasses.
            var liveAgentIds = await GetLiveIdsAsync(_db.Agents, agentIds, cancellationToken);

            var liveToolIds = (await GetLiveIdsAsync(_db.TenantTools, toolIds, cancellationToken))
                .Concat(await GetLiveIdsAsync(_db.MarketplaceTools, toolIds, cancellationToken))
                .Concat(await GetLiveIdsAsync(_db.PublishedTenantTools, toolIds, cancellationToken))
                .Distinct()
                .ToList();

            var liveAiModelIds = await GetLiveIdsAsync(_db.AIModels, aiModelIds, cancellationToken);

            var liveEntityTags = entityTags.Where(t =>
                (string.Equals(t.EntityType, CommonConsts.TaggableEntityTypes.Agent, StringComparison.OrdinalIgnoreCase) && liveAgentIds.Contains(t.EntityId)) ||
                (string.Equals(t.EntityType, CommonConsts.TaggableEntityTypes.Tool, StringComparison.OrdinalIgnoreCase) && liveToolIds.Contains(t.EntityId)) ||
                (string.Equals(t.EntityType, CommonConsts.TaggableEntityTypes.AIModel, StringComparison.OrdinalIgnoreCase) && liveAiModelIds.Contains(t.EntityId))
            ).ToList();

            return new SuccessResult<List<EntityTagResult>>(_mapper.Map<List<EntityTagResult>>(liveEntityTags));
        }

        private static async Task<List<int>> GetLiveIdsAsync<TEntity>(IQueryable<TEntity> dbSet, IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
            where TEntity : Entity<int>
        {
            return await dbSet
                .AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
        }
    }
}
