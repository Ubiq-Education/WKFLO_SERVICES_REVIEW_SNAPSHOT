using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Common
{
    /// <summary>
    /// Resolves the chat icon (<see cref="AgentChatSetting.ChatbotIconPath"/>) configured for
    /// PublishedTenantAgents, so agents created directly in WKFLO (which never had
    /// <see cref="Agent.IconUrl"/> populated) can still expose an icon on the TenantAgent read models.
    /// Mirrors the AI API AgentChatIconHelper ordering rule (widget row first, then most recently
    /// updated), which is not referenced from MD.Application.
    /// </summary>
    public static class AgentChatIconResolver
    {
        /// <summary>
        /// One batched query for all given published agent ids. Only ids that resolve to a non-empty
        /// icon are present in the returned dictionary.
        /// </summary>
        public static async Task<Dictionary<int, string>> GetChatIconsByPublishedAgentIdAsync(
            IMasterDataDbContext db,
            IEnumerable<int> publishedAgentIds,
            CancellationToken cancellationToken = default)
        {
            var ids = publishedAgentIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<int, string>();

            // Ordered in memory, not via SQL ORDER BY: the SQLite provider used by the functional test
            // harness cannot translate ORDER BY on DateTimeOffset expressions (see AgentChatIconHelper).
            var settings = await db.AgentChatSettings
                .AsNoTracking()
                .Where(acs => ids.Contains(acs.AgentId) && !acs.IsDeleted && acs.ChatbotIconPath != null && acs.ChatbotIconPath != "")
                .ToListAsync(cancellationToken);

            // Whitespace-only paths pass the SQL filter above; drop them here so they can never win the
            // widget/recency ordering over a usable icon (matches the write-through's IsNullOrWhiteSpace rule).
            return settings
                .Where(acs => !string.IsNullOrWhiteSpace(acs.ChatbotIconPath))
                .GroupBy(acs => acs.AgentId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(acs => acs.IsWidget)
                          .ThenByDescending(acs => acs.LastUpdatedOn ?? acs.CreatedOn)
                          .First().ChatbotIconPath);
        }
    }
}
