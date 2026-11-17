using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetAvailableSpinUpAgentsByShareId
{
    /// <summary>
    /// Public-scoped counterpart to <see cref="GetAvailableSpinUpAgentsByTenantAgentId.GetAvailableSpinUpAgentsByTenantAgentIdQueryHandler"/>
    /// — resolves the agent via <c>AgentChatSetting.ShareId</c> (the same resolution path every other
    /// public agent endpoint uses, e.g. <c>GetPublicAgentChatSettingQueryHandler</c>), then lists the
    /// name and chat icon URL of every agent that agent is explicitly allowed to notify (the
    /// <c>AgentAllowedSpinUpAgent</c> allowlist).
    /// </summary>
    public class GetAvailableSpinUpAgentsByShareIdQueryHandler : IRequestHandler<GetAvailableSpinUpAgentsByShareIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;

        public GetAvailableSpinUpAgentsByShareIdQueryHandler(IMasterDataDbContext db)
        {
            _db = db;
        }

        public async ValueTask<Result> Handle(GetAvailableSpinUpAgentsByShareIdQuery query, CancellationToken cancellationToken)
        {
            var chatSetting = await _db.AgentChatSettings
                .AsNoTracking()
                .Include(c => c.Agent)
                .Where(c => c.ShareId == query.ShareId && !c.IsDeleted && !c.Agent.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (chatSetting?.Agent is not RunnableAgent currentAgent)
            {
                return new NotFoundResult(nameof(query.ShareId), ErrorConst.AgentNotFound);
            }

            var targets = await GetAllowedSpinUpAgentTargetsAsync(currentAgent.Id, chatSetting.IsWidget, cancellationToken);

            return new SuccessResult<List<AgentSpinUpTargetResult>>(targets);
        }

        // Queries the root Agents DbSet, not .OfType<RunnableAgent>() — RunnableAgent is an abstract
        // intermediate TPH type with no entity mapping of its own, which caused a real EF Core failure
        // earlier in this story (see RunnableAISyncService.cs). Filter/cast to RunnableAgent in memory
        // instead, matching that already-verified pattern (also used by AgentEventPublisherPlugin).
        private async Task<List<AgentSpinUpTargetResult>> GetAllowedSpinUpAgentTargetsAsync(int agentId, bool rootIsWidget, CancellationToken cancellationToken)
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
                var iconUrl = await AgentChatIconHelper.GetChatbotIconUrlAsync(_db, agent.Id, rootIsWidget, cancellationToken);
                targets.Add(new AgentSpinUpTargetResult { Name = agent.Name, IconUrl = iconUrl });
            }

            return targets;
        }
    }
}
