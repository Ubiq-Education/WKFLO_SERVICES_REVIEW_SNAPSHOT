using AMAIS.WKFLO.AI.Application.Agents.Actors;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession
{
    /// <summary>
    /// US 7501 (approved change 3): on Stop, drops the hand-offs still queued in the session's agent actors
    /// (<c>{sessionId}:{agentId}</c>, see <see cref="IAgentActor.ClearQueueAsync"/>).
    /// </summary>
    public interface ISessionHandOffQueueClearer
    {
        Task ClearAsync(string sessionId, int? hostAgentId, string hostShareId, DateTimeOffset stoppedAt, CancellationToken cancellationToken);
    }

    /// <summary>
    /// US 7501 (review fix): only agents of the Stop's tenant are considered, so a Stop never clears another tenant's
    /// queues even for the same session id (an actor id holds the agent id, which belongs to one tenant).
    /// The actors that can hold a queued hand-off of the session are found from existing data, no new store:
    /// the chat's own agent, the agents that already took part in the session (<c>Conversations</c>), and every
    /// agent those may hand off to (<c>AgentAllowedSpinUpAgents</c>, followed transitively, because a hand-off
    /// target that has not answered yet has no conversation row).
    /// </summary>
    public sealed class SessionHandOffQueueClearer : ISessionHandOffQueueClearer
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly IActorProxyFactory _actorProxyFactory;
        private readonly ILogger<SessionHandOffQueueClearer> _logger;
        private readonly IDroppedHandOffMarkerSaver _droppedHandOffMarkerSaver;

        public SessionHandOffQueueClearer(IMasterDataDbContext db, TenantContext tenantContext, IActorProxyFactory actorProxyFactory,
            ILogger<SessionHandOffQueueClearer> logger, IDroppedHandOffMarkerSaver droppedHandOffMarkerSaver)
        {
            _droppedHandOffMarkerSaver = droppedHandOffMarkerSaver;
            _db = db;
            _tenantContext = tenantContext;
            _actorProxyFactory = actorProxyFactory;
            _logger = logger;
        }

        public async Task ClearAsync(string sessionId, int? hostAgentId, string hostShareId, DateTimeOffset stoppedAt, CancellationToken cancellationToken)
        {
            var agentIds = await ResolveSessionAgentIdsAsync(sessionId, hostAgentId, hostShareId, cancellationToken);

            var dropped = await Task.WhenAll(agentIds.Select(agentId => ClearActorQueueAsync(sessionId, agentId, stoppedAt)));

            // US 7502: hand-offs were dropped (whatever their number, one marker per Stop): the chat agent's history
            // keeps the Stop's marker, unless a running turn already saved it.
            if (dropped.Sum() > 0)
            {
                await _droppedHandOffMarkerSaver.SaveAsync(new DroppedHandOffStop(_tenantContext.TenantName, sessionId, stoppedAt)
                {
                    HostAgentId = hostAgentId,
                    HostShareId = hostShareId
                });
            }
        }

        internal async Task<IReadOnlyCollection<int>> ResolveSessionAgentIdsAsync(string sessionId, int? hostAgentId, string hostShareId,
            CancellationToken cancellationToken)
        {
            var agentIds = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.SessionId == sessionId && c.Tenant.Name == _tenantContext.TenantName)
                .Select(c => c.AgentId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (hostAgentId is null && !string.IsNullOrWhiteSpace(hostShareId))
            {
                hostAgentId = await _db.AgentChatSettings
                    .AsNoTracking()
                    .Where(acs => acs.ShareId == hostShareId && acs.Agent.Tenant.Name == _tenantContext.TenantName)
                    .Select(acs => (int?)acs.AgentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var seeds = new List<int>(agentIds);
            if (hostAgentId is { } host)
                seeds.Add(host);

            // Follow the hand-off permissions transitively, same closure as the Stop's ownership check.
            return await HandOffTargetClosure.ExpandAsync(_db, _tenantContext.TenantName, seeds, cancellationToken);
        }

        private async Task<int> ClearActorQueueAsync(string sessionId, int agentId, DateTimeOffset stoppedAt)
        {
            try
            {
                var actor = _actorProxyFactory.CreateActorProxy<IAgentActor>(
                    new ActorId(AgentActorGatekeeper.BuildActorId(sessionId, agentId)), nameof(AgentActor));

                return await actor.ClearQueueAsync(stoppedAt);
            }
            catch (Exception ex)
            {
                // Best effort: the actor's dequeue checkpoint and each hand-off's own start checkpoint still stop them.
                _logger.LogWarning(ex, "Clearing the queued hand-offs of agent {AgentId} in session {SessionId} failed", agentId, sessionId);
                return 0;
            }
        }
    }
}
