using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.MD.Domain.Helpers
{
    /// <summary>
    /// Shared helper used by every runtime code path that renders the chat widget (AI API's public/tenant
    /// chat-setting handlers, MD API's ChatController-backed MVC view) to compute whether the agent has been
    /// tagged with the "Maisy" feature-selection tag. Lives in Common so both services can use it without a
    /// cross-service Application-to-Application project reference.
    /// </summary>
    public static class AgentTagHelper
    {
        public static Task<bool> IsMaisyAgentAsync(IMasterDataDbContext db, int? agentId, CancellationToken cancellationToken)
        {
            if (agentId is null)
                return Task.FromResult(false);

            return db.EntityTags.AsNoTracking()
                .AnyAsync(m => m.EntityType == TaggableEntityTypes.Agent
                            && m.EntityId == agentId
                            && m.Tag.Name == FeatureTagConsts.Maisy, cancellationToken);
        }
    }
}
