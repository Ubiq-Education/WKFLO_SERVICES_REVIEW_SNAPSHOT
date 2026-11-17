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
    public class TenantAISyncService : ITenantAISyncService
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatNotificationService _notificationService;
        private readonly IRunnableAISyncService _runnableAISyncService;
        private readonly TenantContext _tenantContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatTurnFailureState _chatTurnFailureState;

        public TenantAISyncService(IMasterDataDbContext db, IChatNotificationService notificationService,
            IRunnableAISyncService runnableAISyncService, IDateTimeProvider dateTimeProvider, TenantContext tenantContext,
            IChatTurnFailureState chatTurnFailureState = null)
        {
            _chatTurnFailureState = chatTurnFailureState ?? new ChatTurnFailureState();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _runnableAISyncService = runnableAISyncService ?? throw new ArgumentNullException(nameof(runnableAISyncService));
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
            _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        }

        public async Task<TenantAgentData> RunTenantAgentByIdAsync(int agentId, string input, List<FileAttachment> files, string sessionId, bool chainOfThought, bool isContactFormReplay = false, Guid? originalMessageId = null, CancellationToken cancellationToken = default)
        {
            // Set conversation state to processing
            await _notificationService.SendConversationStateUpdate(sessionId, "processing");

            var agent = await _db.TenantAgents
                .Include(m => m.AIModel)
                .ThenInclude(m => m.Secret)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == agentId && !m.IsDeleted && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent != null && agent is RunnableAgent runnableAgent)
            {
                var message = await _runnableAISyncService.RunRunnableAgentAsync(runnableAgent, input, files, sessionId, chainOfThought, isContactFormReplay: isContactFormReplay, originalMessageId: originalMessageId, cancellationToken: cancellationToken);
                return message != null ? new TenantAgentData { Message = message } : null;
            }

            RecordAgentUnavailable(agentId);
            return null;
        }

        public async Task<TenantAgentData> RunVoiceTenantAgentByIdAsync(int agentId, string input, string sessionId, string branchId, bool chainOfThought, CancellationToken cancellationToken = default)
        {
            // Set conversation state to processing
            await _notificationService.SendConversationStateUpdate(sessionId, "processing");

            var agent = await _db.TenantAgents
                .Include(m => m.AIModel)
                .ThenInclude(m => m.Secret)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == agentId && !m.IsDeleted && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent != null && agent is RunnableAgent runnableAgent)
            {
                var message = await _runnableAISyncService.RunRunnableAgentVoiceAsync(runnableAgent, input, sessionId, branchId, chainOfThought, cancellationToken: cancellationToken);
                return message != null ? new TenantAgentData { Message = message } : null;
            }

            RecordAgentUnavailable(agentId);
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
