using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Common;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgents
{
    public class ListTenantAgentsQueryHandler : IRequestHandler<ListTenantAgentsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public ListTenantAgentsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(ListTenantAgentsQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.TenantAgents.AsNoTracking()
                .Include(m => m.AIModel)
                .Include(m => m.PublishedTenantAgent)
                .ThenInclude(m => m.MarketplaceAgent)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Tenant.Name == _tenantContext.TenantName)
                .ToListAsync(cancellationToken);

            var entityIds = result.Select(a => a.Id).ToList();
            var allEntityTags = await _db.EntityTags.AsNoTracking()
                .Include(m => m.Tag)
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.Agent && entityIds.Contains(m.EntityId))
                .ToListAsync(cancellationToken);

            var mappedResults = _mapper.Map<List<TenantAgentGetResult>>(result);

            // Agents created directly in WKFLO only store their icon in AgentChatSetting (keyed to the
            // PublishedTenantAgent), so fall back to it when Agent.IconUrl is empty. Single batched query.
            var publishedIdsByAgentId = result
                .Where(a => string.IsNullOrWhiteSpace(a.IconUrl) && a.PublishedTenantAgent != null)
                .ToDictionary(a => a.Id, a => a.PublishedTenantAgent.Id);
            var chatIcons = await AgentChatIconResolver.GetChatIconsByPublishedAgentIdAsync(_db, publishedIdsByAgentId.Values, cancellationToken);
            foreach (var r in mappedResults)
            {
                r.Tags = _mapper.Map<List<TagGetResult>>(allEntityTags.Where(et => et.EntityId == r.Id).Select(et => et.Tag).ToList());
                if (string.IsNullOrWhiteSpace(r.IconUrl) &&
                    publishedIdsByAgentId.TryGetValue(r.Id, out var publishedId) &&
                    chatIcons.TryGetValue(publishedId, out var chatIcon))
                {
                    r.IconUrl = chatIcon;
                }
            }

            return new SuccessResult<List<TenantAgentGetResult>>(mappedResults);
        }
    }
}
