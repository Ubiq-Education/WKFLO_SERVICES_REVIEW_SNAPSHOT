using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Models.AI;

namespace AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI
{
    public interface IPublicAISyncService
    {
        /// <summary>
        /// <paramref name="originalMessageId"/>: only meaningful alongside
        /// <paramref name="isContactFormReplay"/> — forwarded into
        /// <c>RunTextOptions.OriginalMessageId</c>, see there for why.
        /// </summary>
        Task<PublicAgentData> RunPublicAgentByShareIdAsync(string shareId, string input, List<FileAttachment> files, string sessionId, bool isContactFormReplay = false, Guid? originalMessageId = null, CancellationToken cancellationToken = default);
        Task<PublicAgentData> RunVoicePublicAgentByIdAsync(string shareId, string input, string sessionId, string branchId, bool chainOfThought, CancellationToken cancellationToken = default);
    }
}
