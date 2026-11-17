using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers
{
    /// <summary>
    /// Resolves the (TenantAgent.Id, PublishedTenantAgent.Id, AgentChatSetting.ShareId) triple that every
    /// chatbot analytics DXP event carries, from whatever agent reference a given call site already has —
    /// either a loaded <see cref="Agent"/> instance, or a bare agent id whose concrete type (TenantAgent vs
    /// PublishedTenantAgent) is ambiguous (e.g. <c>Conversation.AgentId</c> can be either).
    /// </summary>
    public static class DxpAgentIdentityResolver
    {
        /// <summary>
        /// Resolves ids from an already-loaded agent instance — no DB round-trip needed since
        /// <see cref="TenantAgent.PublishedTenantAgentId"/>/<see cref="PublishedTenantAgent.CreatedByTenantAgentId"/>
        /// are plain scalar columns, always loaded with the entity.
        /// </summary>
        public static (int? TenantAgentId, int? PublishedTenantAgentId) ResolveIds(Agent agent) => agent switch
        {
            TenantAgent ta => (ta.Id, ta.PublishedTenantAgentId),
            PublishedTenantAgent pa => (pa.CreatedByTenantAgentId, pa.Id),
            _ => (null, null)
        };

        /// <summary>
        /// Resolves ids from a bare agent id of unknown concrete type (e.g. <c>Conversation.AgentId</c>),
        /// trying <c>TenantAgents</c> first, then <c>PublishedTenantAgents</c> — mirrors the resolution
        /// pattern already used by <c>ConversationAnalysisWorkflowActivity</c>.
        /// </summary>
        public static async Task<(int? TenantAgentId, int? PublishedTenantAgentId)> ResolveIdsAsync(
            IMasterDataDbContext db, int agentId, CancellationToken cancellationToken = default)
        {
            var tenantAgent = await db.TenantAgents
                .AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => new { a.Id, a.PublishedTenantAgentId })
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantAgent != null)
                return (tenantAgent.Id, tenantAgent.PublishedTenantAgentId);

            var publishedAgent = await db.PublishedTenantAgents
                .AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => new { a.Id, a.CreatedByTenantAgentId })
                .FirstOrDefaultAsync(cancellationToken);

            return publishedAgent != null
                ? (publishedAgent.CreatedByTenantAgentId, publishedAgent.Id)
                : (null, null);
        }

        /// <summary>
        /// Resolves ids from a public embed slug (<c>AgentChatSetting.ShareId</c>) — the reverse of
        /// <see cref="ResolveShareIdAsync"/>. <c>AgentChatSetting.AgentId</c> is itself of unknown concrete
        /// type (same ambiguity as <c>Conversation.AgentId</c>), so this just looks up that id and reuses
        /// <see cref="ResolveIdsAsync"/> rather than duplicating the TenantAgent/PublishedTenantAgent lookup.
        /// </summary>
        public static async Task<(int? TenantAgentId, int? PublishedTenantAgentId)> ResolveIdsFromShareIdAsync(
            IMasterDataDbContext db, string shareId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(shareId))
                return (null, null);

            var chatSetting = await db.AgentChatSettings
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.ShareId == shareId)
                .Select(s => new { s.AgentId })
                .FirstOrDefaultAsync(cancellationToken);

            return chatSetting != null
                ? await ResolveIdsAsync(db, chatSetting.AgentId, cancellationToken)
                : (null, null);
        }

        /// <summary>
        /// Resolves the public embed slug for whichever of the two ids is known. Returns null if neither id
        /// is known or no <see cref="AgentChatSetting"/> is configured for either.
        /// </summary>
        public static async Task<string> ResolveShareIdAsync(
            IMasterDataDbContext db, int? tenantAgentId, int? publishedTenantAgentId, CancellationToken cancellationToken = default)
        {
            if (tenantAgentId == null && publishedTenantAgentId == null)
                return null;

            return await db.AgentChatSettings
                .AsNoTracking()
                .Where(s => !s.IsDeleted
                    && ((tenantAgentId != null && s.AgentId == tenantAgentId)
                        || (publishedTenantAgentId != null && s.AgentId == publishedTenantAgentId)))
                .Select(s => s.ShareId)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
