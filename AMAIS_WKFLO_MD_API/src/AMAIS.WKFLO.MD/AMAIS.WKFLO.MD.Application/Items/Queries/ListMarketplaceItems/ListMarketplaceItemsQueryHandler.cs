using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Items.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Items.Queries.ListMarketplaceItems
{
    public class ListMarketplaceItemsQueryHandler : IRequestHandler<ListMarketplaceItemsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly UserContext _userContext;

        public ListMarketplaceItemsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, UserContext userContext)
        {
            _db = db;
            _mapper = mapper;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListMarketplaceItemsQuery query, CancellationToken cancellationToken)
        {
            var agentsQuery = _db.MarketplaceAgents.AsNoTracking()
                .Include(m => m.AIModel)
                .Where(m => !m.IsDeleted)
                .Select(m => new MarketplaceItemGetResult()
                {
                    Id = "AIAgent_" + m.Id,
                    Name = m.Name,
                    Description = m.Description,
                    Type = ItemType.Agent.ToString(),
                    Category = "AI Agents",
                    // Live average: MarketplaceAgent.Stars is no longer a stored column - see
                    // ListMarketplaceAgentsQueryHandler's comment for the same computation, kept
                    // consistent here so this combined listing doesn't drift from the dedicated one.
                    // Raw (unrounded) average here; rounded to the nearest whole star below once
                    // materialized, since Math.Round isn't reliably translatable inside this
                    // EF-translated Select. Agents with no ratings yet default to 0.
                    Stars = _db.UserStars.Where(us => us.Type == nameof(MarketplaceAgent) && us.TypeId == m.Id && !us.IsDeleted).Select(us => (decimal?)us.Value).Average() ?? 0,
                    // Live count: MarketplaceAgent.Installations is no longer a stored column - see
                    // ListMarketplaceAgentsQueryHandler's comment for the same computation, kept
                    // consistent here so this combined listing doesn't drift from the dedicated one.
                    Installations = _db.TenantAgents.Count(ta => ta.InstalledFromMarketplaceAgentId == m.Id && !ta.IsDeleted),
                    Price = null,
                    Downloads = _db.TenantAgents.Count(ta => ta.InstalledFromMarketplaceAgentId == m.Id && !ta.IsDeleted),
                    // The tenant that published the item: a share copies the sharing tenant onto the marketplace row,
                    // and an item created directly in the marketplace gets the creating tenant.
                    Vendor = m.Tenant.Name,
                    IsDeleted = m.IsDeleted,
                    CreatedOn = m.CreatedOn,
                    LastUpdatedOn = m.LastUpdatedOn
                });

            var toolsQuery = _db.MarketplaceTools.AsNoTracking()
                .Where(m => !m.IsDeleted)
                .Select(m => new MarketplaceItemGetResult()
                {
                    Id = "Tool_" + m.Id,
                    Name = m.Name,
                    Description = m.Description,
                    Type = ItemType.Tool.ToString(),
                    Category = "Tools",
                    // Live average: MarketplaceTool.Stars is no longer a stored column - see the
                    // agent query above for the same reasoning (raw average here, rounded below).
                    Stars = _db.UserStars.Where(us => us.Type == nameof(MarketplaceTool) && us.TypeId == m.Id && !us.IsDeleted).Select(us => (decimal?)us.Value).Average() ?? 0,
                    Installations = _db.TenantTools.Count(tt => tt.InstalledFromMarketplaceToolId == m.Id && !tt.IsDeleted),
                    Price = null,
                    Downloads = _db.TenantTools.Count(tt => tt.InstalledFromMarketplaceToolId == m.Id && !tt.IsDeleted),
                    // The tenant that published the item: a share copies the sharing tenant onto the marketplace row,
                    // and an item created directly in the marketplace gets the creating tenant.
                    Vendor = m.Tenant.Name,
                    IsDeleted = m.IsDeleted,
                    CreatedOn = m.CreatedOn,
                    LastUpdatedOn = m.LastUpdatedOn
                });
            var results = await agentsQuery.Union(toolsQuery).ToListAsync();

            // Round each item's raw average (computed above) to the nearest whole star now that the
            // results are materialized in memory - matches the rounding applied by the dedicated
            // ListMarketplaceAgentsQueryHandler/ListMarketplaceToolsQueryHandler endpoints.
            results.ForEach(m => m.Stars = Math.Round(m.Stars, MidpointRounding.AwayFromZero));

            var userStars = await _db.UserStars.Where(us => us.UserId == Guid.Parse(_userContext.Guid)).ToListAsync();
            results.ForEach(m =>
            {
                if (userStars.Any(us => "AIAgent_" + us.TypeId == m.Id && us.Type == nameof(MarketplaceAgent)))
                {
                    m.UserClickStar = true;
                }
                else if (userStars.Any(us => "Tool_" + us.TypeId == m.Id && us.Type == nameof(MarketplaceTool)))
                {
                    m.UserClickStar = true;
                }
            });

            var userInstalls = await _db.UserInstalls.Where(us => us.UserId == Guid.Parse(_userContext.Guid)).ToListAsync();
            results.ForEach(m =>
            {
                if (userInstalls.Any(us => "AIAgent_" + us.TypeId == m.Id && us.Type == nameof(MarketplaceAgent)))
                {
                    m.UserClickInstall = true;
                }
                else if (userInstalls.Any(us => "Tool_" + us.TypeId == m.Id && us.Type == nameof(MarketplaceTool)))
                {
                    m.UserClickInstall = true;
                }
            });
            return results == null ? new NotFoundResult() : (Result)new SuccessResult<List<MarketplaceItemGetResult>>(results);
        }
    }
}
