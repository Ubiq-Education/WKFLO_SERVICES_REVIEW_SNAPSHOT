using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.AI;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.Common.AI.Application.Services.AI
{
    public class PublicAISyncService : IPublicAISyncService
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatNotificationService _notificationService;
        private readonly IRunnableAISyncService _runnableAISyncService;
        private readonly TenantContext _tenantContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatTurnFailureState _chatTurnFailureState;

        public PublicAISyncService(IMasterDataDbContext db, IChatNotificationService notificationService,
            IRunnableAISyncService runnableAISyncService, TenantContext tenantContext, IDateTimeProvider dateTimeProvider,
            IChatTurnFailureState chatTurnFailureState = null)
        {
            _chatTurnFailureState = chatTurnFailureState ?? new ChatTurnFailureState();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _runnableAISyncService = runnableAISyncService ?? throw new ArgumentNullException(nameof(runnableAISyncService));
            _tenantContext = tenantContext;
            _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        }

        public async Task<PublicAgentData> RunPublicAgentByShareIdAsync(string shareId, string input, List<FileAttachment> files, string sessionId, bool isContactFormReplay = false, Guid? originalMessageId = null, CancellationToken cancellationToken = default)
        {
            // Set conversation state to processing
            await _notificationService.SendConversationStateUpdate(sessionId, "processing");

            // Check AgentChatSetting
            var chatSetting = await _db.AgentChatSettings
                .Include(acs => acs.Agent)
                .Include(m => m.Agent.AIModel)
                .ThenInclude(m => m.Secret)
                .Include(m => m.Agent.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(acs => acs.ShareId == shareId && !acs.IsDeleted && !acs.Agent.IsDeleted && acs.Agent.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            // Find the runnable agent (which is publicly accessible)
            var agent = chatSetting?.Agent;

            if (agent != null && agent is RunnableAgent runnableAgent && runnableAgent.IsActive)
            {
                var message = await _runnableAISyncService.RunRunnableAgentAsync(runnableAgent, input, files, sessionId, chatSetting.ChainOfThought,
                    noProviderMessage: "AI provider not supported", rootIsWidget: chatSetting.IsWidget, isContactFormReplay: isContactFormReplay, originalMessageId: originalMessageId, cancellationToken: cancellationToken);
                return message != null ? new PublicAgentData { Message = message } : null;
            }

            RecordAgentUnavailable(agent?.Id);
            return null;
        }

        public async Task<PublicAgentData> RunVoicePublicAgentByIdAsync(string shareId, string input, string sessionId, string branchId, bool chainOfThought, CancellationToken cancellationToken = default)
        {
            // Set conversation state to processing
            await _notificationService.SendConversationStateUpdate(sessionId, "processing");

            // Check AgentChatSetting
            var chatSetting = await _db.AgentChatSettings
                .Include(acs => acs.Agent)
                .Include(m => m.Agent.AIModel)
                .ThenInclude(m => m.Secret)
                .Include(m => m.Agent.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(acs => acs.ShareId == shareId && !acs.IsDeleted && !acs.Agent.IsDeleted && acs.Agent.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            // Find the runnable agent (which is publicly accessible)
            var agent = chatSetting?.Agent;

            if (agent != null && agent is RunnableAgent runnableAgent && runnableAgent.IsActive)
            {
                // NOTE: preserves the existing behavior of using chatSetting.ChainOfThought rather than
                // this method's own chainOfThought parameter, exactly as the pre-refactor code did.
                var message = await _runnableAISyncService.RunRunnableAgentVoiceAsync(runnableAgent, input, sessionId, branchId, chatSetting.ChainOfThought,
                    noProviderMessage: "AI provider not supported", rootIsWidget: chatSetting.IsWidget, cancellationToken: cancellationToken);
                return message != null ? new PublicAgentData { Message = message } : null;
            }

            RecordAgentUnavailable(agent?.Id);
            return null;
        }

        /// <summary>
        /// US 7959 / Task 7961: the agent of this turn is missing, deleted, switched off or not runnable. Recorded as
        /// <see cref="ChatErrorCodes.ChatAgentUnavailable"/> for the turn's workflow activity to report (the chat used to
        /// stay "processing").
        /// </summary>
        private void RecordAgentUnavailable(int? agentId) =>
            _chatTurnFailureState.Record(ChatTurnFailure.Create(ChatErrorCodes.ChatAgentUnavailable, agentId));
    }
}
