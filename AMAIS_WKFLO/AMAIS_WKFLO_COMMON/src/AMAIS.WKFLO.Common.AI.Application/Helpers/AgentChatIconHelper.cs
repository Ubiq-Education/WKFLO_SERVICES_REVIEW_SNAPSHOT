using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.Common.AI.Application.Helpers
{
    /// <summary>
    /// Resolves an agent's configured chat icon URL from the existing AgentChatSetting storage
    /// (ChatbotIconPath) — no new entity or migration. Callers that stream multiple SignalR updates
    /// per agent turn (typing indicator, streaming tokens, final message) should resolve the icon URL
    /// once per turn into a local variable and reuse it, rather than calling this per token — see
    /// SemanticKernelAgentService's Run* methods.
    /// </summary>
    public static class AgentChatIconHelper
    {
        /// <summary>
        /// Resolves an agent's chat icon URL. AgentId is NOT unique on AgentChatSetting (e.g. separate
        /// widget vs. non-widget configs — see GetTenantAgentChatSettingQueryHandler), so this always
        /// needs a deterministic order to pick one row.
        /// <para>
        /// When <paramref name="rootIsWidget"/> is supplied (this agent is a subagent invoked via a
        /// spin-up/orchestration hand-off — see <c>AgentEventMessage.RootIsWidget</c> /
        /// <c>TriggerMultiAgentOrchestratorCommand.RootIsWidget</c>), the row matching
        /// that value is preferred, so every agent participating in the same hand-off resolves its icon
        /// in the same widget/non-widget context as the root — falling back to the most-recently-updated
        /// row when this agent has none matching. When null (this agent IS the root, resolving its own
        /// icon directly — no hand-off context to match), behavior is unchanged from before: prefer the
        /// IsWidget row, tie-broken by most-recently-updated.
        /// </para>
        /// </summary>
        public static async Task<string> GetChatbotIconUrlAsync(IMasterDataDbContext db, int agentId, bool? rootIsWidget = null, CancellationToken cancellationToken = default)
        {
            // Ordered in memory, not via SQL ORDER BY: EF Core's SQLite provider (used by the functional
            // test harness) can't translate ORDER BY on DateTimeOffset expressions (LastUpdatedOn/CreatedOn),
            // only SQL Server can. A given agent has at most a handful of AgentChatSetting rows (widget vs.
            // non-widget configs), so pulling them into memory first is cheap and works identically on both
            // providers.
            var settings = await db.AgentChatSettings
                .AsNoTracking()
                .Where(acs => acs.AgentId == agentId && !acs.IsDeleted)
                .ToListAsync(cancellationToken);

            return rootIsWidget.HasValue
                ? settings
                    .OrderByDescending(acs => acs.IsWidget == rootIsWidget.Value)
                    .ThenByDescending(acs => acs.LastUpdatedOn ?? acs.CreatedOn)
                    .Select(acs => acs.ChatbotIconPath)
                    .FirstOrDefault()
                : settings
                    .OrderByDescending(acs => acs.IsWidget)
                    .ThenByDescending(acs => acs.LastUpdatedOn ?? acs.CreatedOn)
                    .Select(acs => acs.ChatbotIconPath)
                    .FirstOrDefault();
        }

        /// <summary>
        /// Resolves whether <paramref name="agentId"/> is currently being viewed via its widget vs.
        /// non-widget <c>AgentChatSetting</c> config — used to capture the root agent's own context once,
        /// at the point a spin-up/orchestration hand-off payload is built, so it can be threaded through
        /// to every subagent the hand-off invokes (see <c>AgentEventMessage.RootIsWidget</c> /
        /// <c>TriggerMultiAgentOrchestratorCommand.RootIsWidget</c>). Tie-broken
        /// by most-recently-updated when the agent has more than one row; null when it has none.
        /// </summary>
        public static async Task<bool?> GetIsWidgetContextAsync(IMasterDataDbContext db, int agentId, CancellationToken cancellationToken = default)
        {
            return await db.AgentChatSettings
                .AsNoTracking()
                .Where(acs => acs.AgentId == agentId && !acs.IsDeleted)
                .OrderByDescending(acs => acs.LastUpdatedOn ?? acs.CreatedOn)
                .Select(acs => (bool?)acs.IsWidget)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
