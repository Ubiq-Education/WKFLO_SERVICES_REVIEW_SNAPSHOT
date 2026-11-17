using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession
{
    /// <summary>US 7502: what is known about a Stop that dropped queued hand-offs.</summary>
    public sealed record DroppedHandOffStop(string TenantName, string SessionId, DateTimeOffset StoppedAt)
    {
        /// <summary>The chat's own agent, when known (tenant chat Stop).</summary>
        public int? HostAgentId { get; init; }

        /// <summary>The chat's share id, when known (public chat Stop).</summary>
        public string HostShareId { get; init; }

        /// <summary>An agent whose queued hand-off was dropped; used to find the chat's agent when nothing else tells it.</summary>
        public int? DroppedAgentId { get; init; }
    }

    /// <summary>
    /// US 7502: when a Stop only dropped queued hand-offs (nothing was running, e.g. MAISY had answered and Kit was still
    /// queued), no turn saves a "Stopped" marker, so the divider would only be live. This saves ONE marker for that Stop
    /// in the chat agent's (host's) history.
    /// </summary>
    public interface IDroppedHandOffMarkerSaver
    {
        /// <summary>Never throws: runs from the background queue clearer and the actor's dequeue check.</summary>
        Task SaveAsync(DroppedHandOffStop stop);
    }

    /// <summary>
    /// Runs in its own DI scope (its callers are a background task and a Dapr actor) with the Stop's tenant and the
    /// session owner's user id, taken from the host's conversation row: the history is the host's own blob, loaded
    /// first and saved through the usual path (ratings preserved). When only the dropped agent is known, the host is the
    /// root of the session's hand-off graph that reaches it (never an intermediate agent). The marker uses the Stop's stoppedAt, and is not
    /// added when the host history already holds a marker of that Stop (a running turn saved it, or another drop of the
    /// same Stop did). Nothing is saved when the host has no conversation row yet: its first turn is still running and
    /// saves its own history.
    /// </summary>
    public sealed class DroppedHandOffMarkerSaver : IDroppedHandOffMarkerSaver
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DroppedHandOffMarkerSaver> _logger;

        public DroppedHandOffMarkerSaver(IServiceScopeFactory scopeFactory, ILogger<DroppedHandOffMarkerSaver> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task SaveAsync(DroppedHandOffStop stop)
        {
            if (stop == null || string.IsNullOrWhiteSpace(stop.TenantName) || string.IsNullOrWhiteSpace(stop.SessionId))
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var services = scope.ServiceProvider;
                var db = services.GetRequiredService<IMasterDataDbContext>();
                var cancellationToken = CancellationToken.None;

                var rows = await db.Conversations
                    .AsNoTracking()
                    .Where(c => c.SessionId == stop.SessionId && c.Tenant.Name == stop.TenantName)
                    .OrderBy(c => c.ConversationDate)
                    .Select(c => new { c.AgentId, c.UserId })
                    .ToListAsync(cancellationToken);

                var hostAgentId = await ResolveHostAgentIdAsync(db, stop, rows.Select(r => r.AgentId).ToList(), cancellationToken);
                var hostRow = hostAgentId is int id ? rows.FirstOrDefault(r => r.AgentId == id) : null;
                if (hostRow == null)
                {
                    _logger.LogInformation("Stop of session {SessionId}: no saved history of the chat agent yet, no marker saved for the dropped hand-offs", stop.SessionId);
                    return;
                }

                var agent = await db.Agents
                    .Where(m => m.Id == hostRow.AgentId && !m.IsDeleted && m.Tenant.Name == stop.TenantName)
                    .FirstOrDefaultAsync(cancellationToken);
                if (agent is not RunnableAgent runnableAgent)
                {
                    _logger.LogInformation("Stop of session {SessionId}: chat agent {AgentId} not found, no marker saved for the dropped hand-offs",
                        stop.SessionId, hostRow.AgentId);
                    return;
                }

                // The host's own blob: same tenant and same user id as its conversation row.
                services.GetRequiredService<TenantContext>().TenantName = stop.TenantName;
                services.GetRequiredService<UserContext>().Guid = hostRow.UserId;
                services.GetRequiredService<ChatContext>().SessionId = stop.SessionId;

                var marker = await services.GetRequiredService<ISemanticKernelAgentService>().SaveStoppedMarkerAsync(runnableAgent, stop.SessionId, stop.StoppedAt);
                _logger.LogInformation("Stop of session {SessionId} (tenant {TenantName}) at {StoppedAt}: Stopped marker for the dropped hand-offs in chat agent {AgentId}: {StoppedMarker}",
                    stop.SessionId, stop.TenantName, stop.StoppedAt, runnableAgent.Id, marker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving the Stopped marker for the dropped hand-offs of session {SessionId} failed", stop.SessionId);
            }
        }

        private static async Task<int?> ResolveHostAgentIdAsync(IMasterDataDbContext db, DroppedHandOffStop stop, List<int> rowAgentIds,
            CancellationToken cancellationToken)
        {
            if (stop.HostAgentId is int host)
                return host;

            if (!string.IsNullOrWhiteSpace(stop.HostShareId))
            {
                return await db.AgentChatSettings
                    .AsNoTracking()
                    .Where(acs => acs.ShareId == stop.HostShareId && !acs.IsDeleted && acs.Agent.Tenant.Name == stop.TenantName)
                    .Select(acs => (int?)acs.AgentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            // Only the dropped agent is known (actor dequeue check). The chat agent is a ROOT of the session's hand-off graph:
            // it reaches the dropped agent (directly or through other hand-offs) and no other agent of the session reaches
            // it. An intermediate agent (MAISY -> Kit -> Vera: Kit for a dropped Vera) is never taken; with no root (e.g. a
            // cycle) the host is unknown and nothing is saved.
            if (stop.DroppedAgentId is not int dropped)
                return null;

            var sessionAgents = rowAgentIds.Distinct().ToList();
            var reachableFrom = new Dictionary<int, HashSet<int>>();
            foreach (var agentId in sessionAgents)
                reachableFrom[agentId] = await HandOffTargetClosure.ExpandAsync(db, stop.TenantName, [agentId], cancellationToken);

            foreach (var candidate in sessionAgents.Where(a => a != dropped))
            {
                var reachesDropped = reachableFrom[candidate].Contains(dropped);
                // The dropped agent itself is ignored: with hand-offs allowed both ways (MAISY <-> Kit), Kit reaching MAISY
                // back does not stop MAISY being the root.
                var reachedByAnother = sessionAgents.Any(other => other != candidate && other != dropped && reachableFrom[other].Contains(candidate));
                if (reachesDropped && !reachedByAnother)
                    return candidate;
            }

            return null;
        }
    }
}
