using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetAvailableSpinUpAgentsByTenantAgentId
{
    /// <summary>
    /// Lists the name and chat icon URL of every agent the given agent is explicitly allowed to notify via
    /// <c>AgentEventPublisherPlugin.PublishAgentEventAsync</c> (the <c>AgentAllowedSpinUpAgent</c>
    /// allowlist) — the discoverability data a UI needs to build an <c>@name</c> target picker with
    /// per-agent icons instead of a generic placeholder.
    /// Not gated by the given agent's own <c>EnableSpinUpAgents</c>: that flag only controls whether
    /// this agent can *call* <c>publish_agent_event</c>, not whether this read-only listing is meaningful.
    /// </summary>
    public class GetAvailableSpinUpAgentsByTenantAgentIdQueryHandler : IRequestHandler<GetAvailableSpinUpAgentsByTenantAgentIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public GetAvailableSpinUpAgentsByTenantAgentIdQueryHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetAvailableSpinUpAgentsByTenantAgentIdQuery query, CancellationToken cancellationToken)
        {
            var currentAgent = await _db.Agents
                .AsNoTracking()
                .Where(a => a.Id == query.Id && !a.IsDeleted && a.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (currentAgent is not RunnableAgent)
            {
                return new NotFoundResult(nameof(query.Id), ErrorConst.AgentNotFound);
            }

            var targets = await GetAllowedSpinUpAgentTargetsAsync(currentAgent.Id, cancellationToken);

            return new SuccessResult<List<AgentSpinUpTargetResult>>(targets);
        }

        // Queries the root Agents DbSet, not .OfType<RunnableAgent>() — RunnableAgent is an abstract
        // intermediate TPH type with no entity mapping of its own, which caused a real EF Core failure
        // earlier in this story (see RunnableAISyncService.cs). Filter/cast to RunnableAgent in memory
        // instead, matching that already-verified pattern (also used by AgentEventPublisherPlugin).
        private async Task<List<AgentSpinUpTargetResult>> GetAllowedSpinUpAgentTargetsAsync(int agentId, CancellationToken cancellationToken)
        {
            var allowedAgentIds = await _db.AgentAllowedSpinUpAgents
                .AsNoTracking()
                .Where(m => m.AgentId == agentId)
                .Select(m => m.AllowedSpinUpAgentId)
                .ToListAsync(cancellationToken);

            if (allowedAgentIds.Count == 0)
                return new List<AgentSpinUpTargetResult>();

            var agents = await _db.Agents
                .AsNoTracking()
                .Where(a => !a.IsDeleted && allowedAgentIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

            var runnableAgents = agents
                .OfType<RunnableAgent>()
                .Where(a => a.IsActive)
                .OrderBy(a => a.Name)
                .ToList();

            var targets = new List<AgentSpinUpTargetResult>(runnableAgents.Count);

            // Resolved sequentially, not with Task.WhenAll: every call shares this handler's single
            // IMasterDataDbContext instance, and EF Core's DbContext is not thread-safe for concurrent use.
            foreach (var agent in runnableAgents)
            {
                var iconUrl = await AgentChatIconHelper.GetChatbotIconUrlAsync(_db, agent.Id, rootIsWidget: null, cancellationToken);
                targets.Add(new AgentSpinUpTargetResult { Name = agent.Name, IconUrl = iconUrl });
            }

            return targets;
        }
    }
}
