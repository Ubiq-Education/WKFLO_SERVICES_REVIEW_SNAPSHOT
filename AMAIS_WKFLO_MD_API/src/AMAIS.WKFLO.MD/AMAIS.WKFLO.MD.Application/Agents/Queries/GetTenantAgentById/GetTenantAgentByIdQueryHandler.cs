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

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentById
{
    public class GetTenantAgentByIdQueryHandler : IRequestHandler<GetTenantAgentByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public GetTenantAgentByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantAgentByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.TenantAgents.AsNoTracking()
                .Include(m => m.AIModel)
                .Include(m => m.PublishedTenantAgent)
                .ThenInclude(m => m.MarketplaceAgent)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == query.Id && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
                return new NotFoundResult();

            var mapped = _mapper.Map<TenantAgentGetResult>(result);

            // Fall back to the chat icon (stored against the PublishedTenantAgent) for agents created
            // directly in WKFLO, whose Agent.IconUrl was never populated.
            if (string.IsNullOrWhiteSpace(mapped.IconUrl) && result.PublishedTenantAgent != null)
            {
                var chatIcons = await AgentChatIconResolver.GetChatIconsByPublishedAgentIdAsync(
                    _db, new[] { result.PublishedTenantAgent.Id }, cancellationToken);
                if (chatIcons.TryGetValue(result.PublishedTenantAgent.Id, out var chatIcon))
                    mapped.IconUrl = chatIcon;
            }

            var entityTags = await _db.EntityTags.AsNoTracking()
                .Include(m => m.Tag)
                .Where(m => m.EntityType == CommonConsts.TaggableEntityTypes.Agent && m.EntityId == result.Id)
                .ToListAsync(cancellationToken);

            mapped.Tags = _mapper.Map<List<TagGetResult>>(entityTags.Select(et => et.Tag).ToList());

            return new SuccessResult<TenantAgentGetResult>(mapped);
        }
    }
}
