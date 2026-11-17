using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession
{
    /// <summary>
    /// US 7501: every agent a chat can reach through hand-offs. Follows <c>AgentAllowedSpinUpAgents</c> transitively
    /// (the chat agent hands off to B, B to C...) until no new agent is found; the visited set also stops cycles. Only
    /// agents of <c>tenantName</c> are followed, so nothing of another tenant is ever reached.
    /// </summary>
    internal static class HandOffTargetClosure
    {
        /// <returns>The <paramref name="seedAgentIds"/> plus every agent reachable from them.</returns>
        public static async Task<HashSet<int>> ExpandAsync(IMasterDataDbContext db, string tenantName, IEnumerable<int> seedAgentIds,
            CancellationToken cancellationToken)
        {
            var known = new HashSet<int>(seedAgentIds);
            var frontier = known.ToList();
            while (frontier.Count > 0)
            {
                var targets = await db.AgentAllowedSpinUpAgents
                    .AsNoTracking()
                    .Where(m => frontier.Contains(m.AgentId) && m.AllowedSpinUpAgent.Tenant.Name == tenantName)
                    .Select(m => m.AllowedSpinUpAgentId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                frontier = targets.Where(known.Add).ToList();
            }

            return known;
        }
    }
}
