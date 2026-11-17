using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.AI.Providers;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.AI
{
    /// <summary>
    /// Looks up against the <c>RunnableAgent</c> base type (covering both <c>TenantAgent</c> and
    /// <c>PublishedTenantAgent</c>) rather than <c>TenantAgent</c> only. Also hosts
    /// <see cref="RunRunnableAgentAsync"/>, the shared execution core reused by
    /// <see cref="TenantAISyncService.RunTenantAgentByIdAsync"/> and
    /// <c>PublicAISyncService.RunPublicAgentByShareIdAsync</c> once each has done its own distinct
    /// lookup — this is the one place that logic lives, rather than being duplicated per service.
    /// </summary>
    public class RunnableAISyncService : IRunnableAISyncService
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly ISemanticKernelAgentService _semanticKernelAgentService;
        private readonly IChatNotificationService _notificationService;
        private readonly TenantContext _tenantContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatTurnFailureState _chatTurnFailureState;
        private readonly ChatContext _chatContext;

        public RunnableAISyncService(IMasterDataDbContext db, IWkfloMapper mapper, ISemanticKernelAgentService semanticKernelAgentService,
            IChatNotificationService notificationService, IDateTimeProvider dateTimeProvider, TenantContext tenantContext,
            IChatTurnFailureState chatTurnFailureState = null, ChatContext chatContext = null)
        {
            // US 7959 / Task 7961: without a scope's state (e.g. some tests) failures are only logged.
            _chatTurnFailureState = chatTurnFailureState ?? new ChatTurnFailureState();
            _chatContext = chatContext ?? new ChatContext();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _semanticKernelAgentService = semanticKernelAgentService ?? throw new ArgumentNullException(nameof(semanticKernelAgentService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
            _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        }

        /// <summary>
        /// Resolves the agent's configured chat icon URL (AgentChatSetting.ChatbotIconPath).
        /// <paramref name="rootIsWidget"/>: see <see cref="IRunnableAISyncService.RunRunnableAgentByIdAsync"/>.
        /// </summary>
        private Task<string> GetChatbotIconUrlAsync(int agentId, bool? rootIsWidget, CancellationToken cancellationToken) =>
            AgentChatIconHelper.GetChatbotIconUrlAsync(_db, agentId, rootIsWidget, cancellationToken);

        public Task<RunnableAgentData> RunRunnableAgentByIdAsync(int agentId, string input, List<FileAttachment> files, string sessionId, bool chainOfThought, bool? rootIsWidget = null, CancellationToken cancellationToken = default) =>
            RunRunnableAgentByIdAsync(agentId, input, files, sessionId, chainOfThought, rootIsWidget, approvalRootAgentId: null, cancellationToken);

        public async Task<RunnableAgentData> RunRunnableAgentByIdAsync(int agentId, string input, List<FileAttachment> files, string sessionId, bool chainOfThought, bool? rootIsWidget, int? approvalRootAgentId, CancellationToken cancellationToken = default)
        {
            // Set conversation state to processing
            await _notificationService.SendConversationStateUpdate(sessionId, "processing");

            // Query the root Agents DbSet, not .OfType<RunnableAgent>() — RunnableAgent is an abstract
            // intermediate TPH type with no discriminator/entity mapping of its own, and EF Core's
            // NavigationExpandingExpressionVisitor cannot apply Include() after OfType<T>() against
            // such a type ("the source is not a queryable of a known entity type"). Agent (the root)
            // is the actual mapped entity, so Include() works there; downcast after materializing,
            // matching the pattern TenantAISyncService already uses against its own concrete DbSet.
            var agent = await _db.Agents
                .Include(m => m.AIModel)
                .ThenInclude(m => m.Secret)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == agentId && !m.IsDeleted && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent is RunnableAgent runnableAgent)
            {
                var message = await RunCoreAsync(runnableAgent, input, files, sessionId, chainOfThought, rootIsWidget,
                    isContactFormReplay: false, originalMessageId: null, toolApprovalResume: null, cancellationToken, approvalRootAgentId);
                return message != null ? new RunnableAgentData { Message = message } : null;
            }

            return null;
        }

        public Task<string> RunRunnableAgentAsync(RunnableAgent runnableAgent, string input, List<FileAttachment> files, string sessionId, bool chainOfThought, string noProviderMessage = "No provider found!", bool? rootIsWidget = null, bool isContactFormReplay = false, Guid? originalMessageId = null, CancellationToken cancellationToken = default) =>
            RunCoreAsync(runnableAgent, input, files, sessionId, chainOfThought, rootIsWidget, isContactFormReplay, originalMessageId, toolApprovalResume: null, cancellationToken);

        public async Task<string> ResumeAfterToolApprovalAsync(ToolApprovalRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);

            // Same root-Agents lookup as RunRunnableAgentByIdAsync (Include() does not work after OfType<RunnableAgent>()).
            // The tenant comes from the stored record, so a decision can only ever resume its own tenant's agent.
            var agent = await _db.Agents
                .Include(m => m.AIModel)
                .ThenInclude(m => m.Secret)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == record.AgentId && !m.IsDeleted && m.Tenant.Name == record.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent is not RunnableAgent runnableAgent || !await CanResumeAsync(record, cancellationToken))
                return null;

            await _notificationService.SendConversationStateUpdate(record.SessionId, "processing");

            return await RunCoreAsync(runnableAgent, string.Empty, [], record.SessionId, chainOfThought: false,
                rootIsWidget: record.Chat?.RootIsWidget, isContactFormReplay: false, originalMessageId: null, toolApprovalResume: record, cancellationToken,
                approvalRootAgentId: record.RootAgentId);
        }

        public async Task<bool> CanResumeAsync(ToolApprovalRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);

            var agent = await _db.Agents
                .Where(m => m.Id == record.AgentId && !m.IsDeleted && m.Tenant.Name == record.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            // The agent must still exist and be switched on.
            if (agent is not RunnableAgent { IsActive: true })
                return false;

            // Bug 7722: a sub-agent's request belongs to the root agent's chat. The root (the agent that handed off - the
            // id the hand-off ran as, tenant or published) must still exist in this tenant, be switched on and still have
            // hand-offs on; and the sub-agent must still be an active, non-deleted target on the root's allowlist.
            // Loaded and pattern-matched like the sub-agent above: IsActive/EnableSpinUpAgents live on the unmapped
            // abstract RunnableAgent, so they are checked after materializing rather than in the SQL predicate.
            if (record.RootAgentId.HasValue)
            {
                var root = await _db.Agents
                    .Where(m => m.Id == record.RootAgentId.Value && !m.IsDeleted && m.Tenant.Name == record.TenantName)
                    .FirstOrDefaultAsync(cancellationToken);

                if (root is not RunnableAgent { IsActive: true, EnableSpinUpAgents: true })
                    return false;

                if (!await SemanticKernel.SemanticKernelAgentService
                        .WhereActiveAllowedSpinUpAgents(_db.AgentAllowedSpinUpAgents, record.RootAgentId.Value)
                        .AnyAsync(m => m.AllowedSpinUpAgentId == record.AgentId, cancellationToken))
                    return false;
            }

            // A shared/public chat must still be shared, and the share must still point at the chat's agent (the root
            // for a sub-agent's request).
            if (!string.IsNullOrWhiteSpace(record.ShareId))
            {
                var chatAgentId = record.RootAgentId ?? record.AgentId;
                return await _db.AgentChatSettings
                    .AnyAsync(acs => acs.ShareId == record.ShareId && !acs.IsDeleted && acs.AgentId == chatAgentId, cancellationToken);
            }

            return true;
        }

        /// <remarks>
        /// US 7959 (Tasks 7961 / 7963): a missing provider or an empty answer is recorded in the scope's
        /// <see cref="IChatTurnFailureState"/> (never sent as an answer text) for whoever owns the run to report: the
        /// single-agent workflow activity, the multi-agent / hand-off invocation, or the tool approval resume.
        /// </remarks>
        private async Task<string> RunCoreAsync(RunnableAgent runnableAgent, string input, List<FileAttachment> files, string sessionId, bool chainOfThought,
            bool? rootIsWidget, bool isContactFormReplay, Guid? originalMessageId, ToolApprovalRecord toolApprovalResume,
            CancellationToken cancellationToken, int? approvalRootAgentId = null)
        {
            // Resolved once per run so every typing-indicator/message notification below carries the
            // same icon, rather than re-querying per call.
            var chatbotIconUrl = await GetChatbotIconUrlAsync(runnableAgent.Id, rootIsWidget, cancellationToken);

            // Show typing indicator
            await _notificationService.SendTypingIndicator(sessionId, true, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl);

            var agentTagNames = await _db.EntityTags
                .Where(m => m.EntityType == TaggableEntityTypes.Agent && m.EntityId == runnableAgent.Id)
                .Select(m => m.Tag.Name)
                .ToListAsync(cancellationToken);

            // Resolve once per run so the tool-disclosure guidance in the LLM Instructions can be
            // qualified for non-Maisy agents (Task #7032) without changing Maisy behavior.
            var isMaisyAgent = await AgentTagHelper.IsMaisyAgentAsync(_db, runnableAgent.Id, cancellationToken);

            // Update last run time
            runnableAgent.LastRunAt = _dateTimeProvider.DateTimeOffsetUtcNow();
            await _db.SaveChangesAsync(cancellationToken);

            // Get AI provider for the agent
            AIBaseProvider provider = AIProviderHelper.GetProvider(runnableAgent);

            if (provider == null)
                return await RecordNoProviderAsync(runnableAgent, sessionId, chatbotIconUrl);

            // Run the agent using the semantic kernel service
            var agentAnswer = await _semanticKernelAgentService.RunTextAsync(new RunTextOptions()
            {
                Message = input,
                Files = files,
                SessionId = sessionId,
                Agent = runnableAgent,
                AgentTagNames = agentTagNames,
                Provider = provider,
                ChainOfThought = chainOfThought,
                RootIsWidget = rootIsWidget,
                IsMaisyAgent = isMaisyAgent,
                IsContactFormReplay = isContactFormReplay,
                OriginalMessageId = originalMessageId,
                ToolApprovalResume = toolApprovalResume,
                ApprovalsAvailable = toolApprovalResume != null || approvalRootAgentId != null,
                RootAgentId = approvalRootAgentId
            }, cancellationToken);

            if (agentAnswer != null)
            {
                // Send the response message
                var responseMessage = new ChatMessage
                {
                    Id = agentAnswer.MessageId,
                    Type = "assistant",
                    Content = agentAnswer.Message,
                    ChannelMessageType = ChannelMessageType.Normal,
                    Timestamp = _dateTimeProvider.DateTimeOffsetUtcNow()
                };

                if (!string.IsNullOrEmpty(agentAnswer.Message))
                {
                    await _notificationService.SendMessageToChat(sessionId, responseMessage, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl);
                    _chatTurnFailureState.MarkAnswerDelivered();
                }
                else
                {
                    RecordEmptyAnswer(runnableAgent, chatbotIconUrl);
                }

                // Stop typing indicator
                await _notificationService.SendTypingIndicator(sessionId, false, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl);

                // Set conversation state to idle
                await _notificationService.SendConversationStateUpdate(sessionId, "idle");

                return agentAnswer.Message;
            }

            return null;
        }

        public async Task<string> RunRunnableAgentVoiceAsync(RunnableAgent runnableAgent, string input, string sessionId, string branchId, bool chainOfThought, string noProviderMessage = "No provider found!", bool? rootIsWidget = null, CancellationToken cancellationToken = default)
        {
            // Resolved once per run so every typing-indicator/message notification below carries the
            // same icon, rather than re-querying per call.
            var chatbotIconUrl = await GetChatbotIconUrlAsync(runnableAgent.Id, rootIsWidget, cancellationToken);

            // Show typing indicator
            await _notificationService.SendTypingIndicator(sessionId, true, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl);

            var agentTagNames = await _db.EntityTags
                .Where(m => m.EntityType == TaggableEntityTypes.Agent && m.EntityId == runnableAgent.Id)
                .Select(m => m.Tag.Name)
                .ToListAsync(cancellationToken);

            // Resolve once per run so the tool-disclosure guidance in the LLM Instructions can be
            // qualified for non-Maisy agents (Task #7032) without changing Maisy behavior.
            var isMaisyAgent = await AgentTagHelper.IsMaisyAgentAsync(_db, runnableAgent.Id, cancellationToken);

            // Update last run time
            runnableAgent.LastRunAt = _dateTimeProvider.DateTimeOffsetUtcNow();
            await _db.SaveChangesAsync(cancellationToken);

            // Get AI provider for the agent
            AIBaseProvider provider = AIProviderHelper.GetProvider(runnableAgent);

            if (provider == null)
            {
                return await RecordNoProviderAsync(runnableAgent, sessionId, chatbotIconUrl);
            }

            // Resolve voice settings from agent tool config
            var voiceName = "en-US-JennyNeural";
            var voiceLangCode = "en-US";
            var amaisTool = runnableAgent.AgentTools?.FirstOrDefault(t => t.Tool.Type == CommonToolTypeConsts.IntegrationAmais)?.Tool;

            if (amaisTool != null)
            {
                var toolConfig = _mapper.Map<ToolResult>(amaisTool);
                voiceName = toolConfig.AmaisIntegrationToolConfiguration?.VoiceName ?? voiceName;
                voiceLangCode = toolConfig.AmaisIntegrationToolConfiguration?.VoiceLanguageCode ?? voiceLangCode;
            }

            // Run the agent using the semantic kernel service
            var agentAnswer = await _semanticKernelAgentService.RunVoiceAsync(new RunVoiceOptions()
            {
                Message = input,
                SessionId = sessionId,
                Agent = runnableAgent,
                AgentTagNames = agentTagNames,
                Provider = provider,
                BranchId = branchId,
                VoiceName = voiceName,
                VoiceLangCode = voiceLangCode,
                ChainOfThought = chainOfThought,
                RootIsWidget = rootIsWidget,
                IsMaisyAgent = isMaisyAgent
            }, cancellationToken);

            if (agentAnswer != null)
            {
                // Send the response message
                var responseMessage = new ChatMessage
                {
                    Id = agentAnswer.MessageId,
                    Type = "assistant",
                    Content = agentAnswer.Message,
                    ChannelMessageType = ChannelMessageType.Normal,
                    Timestamp = _dateTimeProvider.DateTimeOffsetUtcNow()
                };

                if (!string.IsNullOrEmpty(agentAnswer.Message))
                {
                    await _notificationService.SendMessageToChat(sessionId, responseMessage, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl);
                    _chatTurnFailureState.MarkAnswerDelivered();
                }
                else
                {
                    RecordEmptyAnswer(runnableAgent, chatbotIconUrl);
                }

                // Stop typing indicator
                await _notificationService.SendTypingIndicator(sessionId, false, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl);

                // Set conversation state to idle
                await _notificationService.SendConversationStateUpdate(sessionId, "idle");

                return agentAnswer.Message;
            }

            return null;
        }

        /// <summary>
        /// US 7959 / Task 7961: the agent has no usable AI provider. Recorded as
        /// <see cref="ChatErrorCodes.ChatModelConfigInvalid"/> for the turn's workflow activity to report (the old
        /// "no provider" text was returned as if it were the answer, and the chat stayed "processing"). Its thinking
        /// bubble stops here. Returns null: there is no answer.
        /// </summary>
        private async Task<string> RecordNoProviderAsync(RunnableAgent runnableAgent, string sessionId, string chatbotIconUrl)
        {
            _chatTurnFailureState.Record(ChatTurnFailure.Create(ChatErrorCodes.ChatModelConfigInvalid, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl));
            await _notificationService.SendTypingIndicator(sessionId, false, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl);
            return null;
        }

        /// <summary>
        /// US 7959 / Task 7961: the run produced no answer text. Unless something else reached the chat this turn (tool
        /// cards / post messages, a contact form, a tool approval request) or a failure was already recorded, that is
        /// <see cref="ChatErrorCodes.ChatModelEmptyResponse"/>.
        /// </summary>
        private void RecordEmptyAnswer(RunnableAgent runnableAgent, string chatbotIconUrl)
        {
            if (_chatTurnFailureState.HasFailure || _chatTurnFailureState.AnswerDelivered
                || _chatContext.ToolApprovalRequestedThisTurn || _chatContext.ContactFormRequestedThisTurn)
                return;

            _chatTurnFailureState.Record(ChatTurnFailure.Create(ChatErrorCodes.ChatModelEmptyResponse, runnableAgent.Id, runnableAgent.Name, chatbotIconUrl));
        }
    }
}
