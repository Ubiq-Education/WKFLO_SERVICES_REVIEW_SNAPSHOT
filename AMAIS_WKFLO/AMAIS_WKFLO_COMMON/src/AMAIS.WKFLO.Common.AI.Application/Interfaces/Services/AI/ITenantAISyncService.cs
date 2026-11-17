using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Models.AI;

namespace AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI
{
    public interface ITenantAISyncService
    {
        /// <summary>
        /// <paramref name="isContactFormReplay"/>: forwarded from
        /// <c>MessageTenantAgentByIdCommand.IsContactFormReplay</c> down into
        /// <c>RunTextOptions.IsContactFormReplay</c> — see there for what honoring it actually does.
        /// <paramref name="originalMessageId"/>: only meaningful alongside
        /// <paramref name="isContactFormReplay"/> — forwarded into
        /// <c>RunTextOptions.OriginalMessageId</c>, see there for why.
        /// </summary>
        Task<TenantAgentData> RunTenantAgentByIdAsync(int agentId, string input, List<FileAttachment> files, string sessionId, bool chainOfThought, bool isContactFormReplay = false, Guid? originalMessageId = null, CancellationToken cancellationToken = default);
        Task<TenantAgentData> RunVoiceTenantAgentByIdAsync(int agentId, string input, string sessionId, string branchId, bool chainOfThought, CancellationToken cancellationToken = default);
    }
}
