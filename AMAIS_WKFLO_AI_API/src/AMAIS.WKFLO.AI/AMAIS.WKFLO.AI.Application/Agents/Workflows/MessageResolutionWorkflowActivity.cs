using AMAIS.WKFLO.AI.Application.Agents.Events.MessageResolutionAnalyzed;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using Dapr.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows
{
    /// <summary>
    /// Judges a single question/answer exchange with ONE lightweight LLM call, then writes the verdict to
    /// <c>MessageTopicClassification.IsResolved</c> and re-reports it to DXP. Mirrors
    /// <see cref="ConversationAnalysisWorkflowActivity"/>'s shape (loaded agent → kernel → single completion
    /// call → defensive parsing → persist → report) but at message granularity and with no debounce — this
    /// activity is expected to run right after the chat response, not 30 minutes later.
    /// </summary>
    public class MessageResolutionWorkflowActivity : WorkflowActivity<MessageResolutionWorkflowInput, bool>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly ILogger<MessageResolutionWorkflowActivity> _logger;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IWkfloMediator _mediator;

        public MessageResolutionWorkflowActivity(
            IMasterDataDbContext db,
            IChatNotificationService notificationService,
            ChatContext chatContext,
            ILogger<MessageResolutionWorkflowActivity> logger,
            IDateTimeProvider dateTimeProvider,
            IWkfloMediator mediator)
        {
            _db = db;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
            _mediator = mediator;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, MessageResolutionWorkflowInput input)
        {
            try
            {
                var classification = await _db.MessageTopicClassifications
                    .FirstOrDefaultAsync(m => m.MessageId == input.MessageId, CancellationToken.None);

                if (classification == null)
                {
                    // Legitimate no-op — see MessageTopicClassification.IsResolved's doc comment: this
                    // message's topic classification never got persisted (e.g. no ChatClassification tool
                    // configured for this agent), so there is nothing to judge or update.
                    _logger.LogInformation("MessageResolution: no MessageTopicClassification row for message {MessageId}; skipping.", input.MessageId);
                    return true;
                }

                if (classification.IsResolved.HasValue)
                {
                    // Already judged — same dedup guard ConversationAnalysisWorkflowActivity's backfill uses
                    // (IsResolved.HasValue), applied symmetrically here so the "exactly once per message"
                    // invariant holds structurally rather than relying on the backfill's 30-min debounce
                    // always outrunning this near-immediate path.
                    _logger.LogInformation("MessageResolution: message {MessageId} already judged; skipping.", input.MessageId);
                    return true;
                }

                var agent = (RunnableAgent)await _db.TenantAgents
                    .Include(a => a.AIModel).ThenInclude(m => m.Secret)
                    .FirstOrDefaultAsync(a => a.Id == input.AgentId)
                    ?? (RunnableAgent)await _db.PublishedTenantAgents
                    .Include(a => a.AIModel).ThenInclude(m => m.Secret)
                    .FirstOrDefaultAsync(a => a.Id == input.AgentId);

                if (agent == null || !agent.IsActive)
                {
                    _logger.LogInformation("MessageResolution: agent {AgentId} not found or inactive; skipping judgment for message {MessageId}.", input.AgentId, input.MessageId);
                    return true;
                }

                var provider = AIProviderHelper.GetProvider(agent);
                if (provider == null)
                {
                    _logger.LogInformation("MessageResolution: no AI provider resolved for agent {AgentId}; skipping judgment for message {MessageId}.", input.AgentId, input.MessageId);
                    return true;
                }

                var kernel = AIProviderHelper.GetKernel(provider, _notificationService, _dateTimeProvider, _chatContext);
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                var analysisHistory = new ChatHistory();
                analysisHistory.AddSystemMessage(
                    "You are analyzing a single question-and-answer exchange from a support or assistant conversation. " +
                    "Based on the exchange below, determine whether the user's question was fully resolved by the assistant's answer. " +
                    "Respond with only a valid JSON object in this exact format: {\"isResolved\": true} or {\"isResolved\": false}. " +
                    "No other text.");
                analysisHistory.AddUserMessage($"User: {input.Question}\nAssistant: {input.Answer}");

                var response = await chatCompletionService.GetChatMessageContentAsync(analysisHistory);
                var isResolved = ParseIsResolved(response?.Content);

                await PersistJudgmentAndReportAsync(classification, agent, input, isResolved);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MessageResolution: failed for message {MessageId}.", input.MessageId);
                return false;
            }
        }

        /// <summary>
        /// Everything downstream of the LLM's verdict: the <c>MessageTopicClassification.IsResolved</c>
        /// UPDATE and the DXP re-report. Split out from <see cref="RunAsync"/> (internal, not private) so it
        /// can be unit-tested directly against a real judgment value, without needing a working AI provider —
        /// <see cref="RunAsync"/> itself can't be exercised end-to-end in a unit test since it makes a real
        /// LLM call via <c>AIProviderHelper.GetKernel</c>.
        /// </summary>
        internal async Task<bool> PersistJudgmentAndReportAsync(MessageTopicClassification classification, RunnableAgent agent, MessageResolutionWorkflowInput input, bool isResolved)
        {
            // !m.IsResolved.HasValue is enforced in the UPDATE predicate itself, not just in RunAsync's own
            // earlier read-then-decide check — the two loads (RunAsync's initial read, this ExecuteUpdateAsync)
            // are not atomic with each other, so the backfill could judge the SAME message in between them.
            // Without this, this near-immediate path could overwrite an already-persisted verdict and publish
            // a second DXP resolution event for the same message (found via a Copilot PR review).
            var updated = await _db.MessageTopicClassifications
                .Where(m => m.MessageId == input.MessageId && !m.IsResolved.HasValue)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsResolved, isResolved)
                    .SetProperty(m => m.AnswerMessageId, input.AnswerMessageId));

            if (updated == 0)
            {
                // Either the row no longer exists (deleted concurrently) or it was already judged by a
                // concurrent path (the backfill, or a duplicate scheduling of this same activity) in the
                // narrow window since RunAsync's own read. Either way: nothing to report, and reporting here
                // would risk a duplicate DXP event for a message the other path already reported.
                _logger.LogInformation("MessageResolution: MessageTopicClassification row for message {MessageId} no longer exists or was already judged; update was a no-op.", input.MessageId);
                return false;
            }

            _logger.LogInformation("MessageResolution: message {MessageId} judged — isResolved={IsResolved}.", input.MessageId, isResolved);

            await TryReportToDxpAsync(classification, agent, input, isResolved);

            return true;
        }

        // DXP reporting is isolated in its own try/catch so a failure here (network error, missing
        // DxpIntegration, etc.) never undoes or masks the successful MessageTopicClassification.IsResolved
        // write above — log and continue, same convention as every other Report*Async call site.
        internal async Task TryReportToDxpAsync(MessageTopicClassification classification, RunnableAgent agent, MessageResolutionWorkflowInput input, bool isResolved)
        {
            try
            {
                var conversation = await _db.Conversations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.SessionId == input.SessionId && c.AgentId == input.AgentId);

                var (tenantAgentId, publishedTenantAgentId) = DxpAgentIdentityResolver.ResolveIds(agent);

                await _mediator.Publish(new MessageResolutionAnalyzedDomainEvent(
                    input.TenantName,
                    input.SessionId,
                    conversation?.SubjectId,
                    tenantAgentId,
                    publishedTenantAgentId,
                    input.MessageId.ToString(),
                    classification.TopicId,
                    classification.TopicName,
                    isResolved,
                    input.Question,
                    input.Answer,
                    conversation?.IsTestChat ?? false,
                    classification.MessageDate,
                    conversation?.ConversationDate,
                    classification.TopicVersion,
                    input.AnswerMessageId.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MessageResolution: failed to report resolution to DXP for message {MessageId}.", input.MessageId);
            }
        }

        internal static bool ParseIsResolved(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return true;

            try
            {
                var doc = JsonDocument.Parse(content.Trim());
                if (doc.RootElement.TryGetProperty("isResolved", out var prop))
                    return prop.GetBoolean();
            }
            catch
            {
                // If parsing fails, default to resolved — same convention as ConversationAnalysisWorkflowActivity.
            }

            return true;
        }
    }
}
