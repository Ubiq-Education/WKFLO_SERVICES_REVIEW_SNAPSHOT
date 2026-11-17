using AMAIS.WKFLO.AI.Application.Agents.Events.ConversationResolutionAnalyzed;
using AMAIS.WKFLO.AI.Application.Agents.Events.MessageResolutionAnalyzed;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
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
    public class ConversationAnalysisWorkflowActivity : WorkflowActivity<ConversationAnalysisWorkflowInput, bool>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatStoreService _chatStoreService;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly ILogger<ConversationAnalysisWorkflowActivity> _logger;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IWkfloMediator _mediator;

        public ConversationAnalysisWorkflowActivity(
            IMasterDataDbContext db,
            IChatStoreService chatStoreService,
            IChatNotificationService notificationService,
            ChatContext chatContext,
            ILogger<ConversationAnalysisWorkflowActivity> logger,
            IDateTimeProvider dateTimeProvider,
            IWkfloMediator mediator)
        {
            _db = db;
            _chatStoreService = chatStoreService;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
            _mediator = mediator;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, ConversationAnalysisWorkflowInput input)
        {
            Conversation conversation = null;
            try
            {
                // Atomic claim: only proceed if still Pending
                var claimed = await _db.Conversations
                    .Where(c => c.SessionId == input.SessionId
                             && c.AgentId == input.AgentId
                             && c.AnalysisStatus == ConversationAnalysisStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.AnalysisStatus, ConversationAnalysisStatus.Processing));

                if (claimed == 0)
                {
                    _logger.LogInformation("ConversationAnalysis: session {SessionId} already claimed or not pending, skipping.", input.SessionId);
                    return true;
                }

                conversation = await _db.Conversations
                    .FirstOrDefaultAsync(c => c.SessionId == input.SessionId && c.AgentId == input.AgentId);

                if (conversation == null)
                    return true;

                var agent = (RunnableAgent)await _db.TenantAgents
                    .Include(a => a.AIModel).ThenInclude(m => m.Secret)
                    .FirstOrDefaultAsync(a => a.Id == input.AgentId)
                    ?? (RunnableAgent)await _db.PublishedTenantAgents
                    .Include(a => a.AIModel).ThenInclude(m => m.Secret)
                    .FirstOrDefaultAsync(a => a.Id == input.AgentId);

                if (agent == null || !agent.IsActive)
                {
                    await CompleteAnalysisAsync(conversation, isResolved: true, agent, input.TenantName, CancellationToken.None);
                    return true;
                }

                var (_, _, chatHistory, _) = await _chatStoreService.LoadChatAsync(input.TenantName, input.AgentId, input.SessionId, input.UserId);

                if (chatHistory == null || chatHistory.Count == 0)
                {
                    await CompleteAnalysisAsync(conversation, isResolved: true, agent, input.TenantName, CancellationToken.None);
                    return true;
                }

                var provider = AIProviderHelper.GetProvider(agent);
                if (provider == null)
                {
                    await CompleteAnalysisAsync(conversation, isResolved: true, agent, input.TenantName, CancellationToken.None);
                    return true;
                }

                var kernel = AIProviderHelper.GetKernel(provider, _notificationService, _dateTimeProvider, _chatContext);
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                var analysisHistory = new ChatHistory();
                analysisHistory.AddSystemMessage(
                    "You are analyzing a support or assistant conversation. " +
                    "Based on the conversation below, determine whether the user's questions or issues were fully resolved by the assistant. " +
                    "Each user message is prefixed with its own [MessageId: <guid>] marker. " +
                    "Respond with only a valid JSON object in this exact format: " +
                    "{\"isResolved\": true, \"messageResolutions\": [{\"messageId\": \"<guid>\", \"isResolved\": true}]}. " +
                    "The top-level \"isResolved\" judges the conversation as a whole. " +
                    "\"messageResolutions\" must include one entry for every [MessageId: ...] marker found, judging whether " +
                    "THAT SPECIFIC user question was resolved by the assistant's reply to it. " +
                    "No other text.");

                var conversationText = BuildConversationText(chatHistory);
                analysisHistory.AddUserMessage(conversationText);

                var response = await chatCompletionService.GetChatMessageContentAsync(analysisHistory);
                var isResolved = ParseIsResolved(response?.Content);

                await CompleteAnalysisAsync(conversation, isResolved, agent, input.TenantName, CancellationToken.None);

                _logger.LogInformation("ConversationAnalysis: session {SessionId} analyzed — isResolved={IsResolved}.", input.SessionId, isResolved);

                // Low-cost backfill mechanism (US 7142): a per-message resolution breakdown, piggy-backed on
                // this SAME LLM call. Fully isolated from the conversation-level result above — a malformed or
                // absent "messageResolutions" field must NEVER affect isResolved (already computed and
                // persisted by CompleteAnalysisAsync) or throw out of this activity.
                try
                {
                    await PersistMessageResolutionsAsync(response?.Content, conversation, agent, input.TenantName, chatHistory, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ConversationAnalysis: failed to parse/persist per-message resolutions for session {SessionId}; conversation-level result is unaffected.", input.SessionId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConversationAnalysis: failed for session {SessionId}.", input.SessionId);

                if (conversation != null)
                {
                    try
                    {
                        conversation.AnalysisStatus = ConversationAnalysisStatus.Pending;
                        await _db.SaveChangesAsync(CancellationToken.None);
                    }
                    catch (Exception saveEx)
                    {
                        _logger.LogError(saveEx, "ConversationAnalysis: failed to reset AnalysisStatus for session {SessionId}.", input.SessionId);
                    }
                }

                return false;
            }
        }

        private async Task CompleteAnalysisAsync(Conversation conversation, bool isResolved, RunnableAgent agent, string tenantName, CancellationToken cancellationToken)
        {
            var analyzedAt = _dateTimeProvider.DateTimeOffsetUtcNow();

            var analysis = new ResolvedConversationAnalysisResult
            {
                ConversationId = conversation.Id,
                IsResolved = isResolved,
                AnalyzedAt = analyzedAt
            };

            await _db.ConversationAnalyses.AddAsync(analysis, cancellationToken);

            conversation.IsResolved = isResolved;
            conversation.AnalysisStatus = ConversationAnalysisStatus.Completed;

            await _db.SaveChangesAsync(cancellationToken);

            // agent can be null here (e.g. deleted/inactive agent) — the event still fires with whatever
            // identity is known, since DXP's counting relies on the session id correlation, not the agent ids.
            var (tenantAgentId, publishedTenantAgentId) = agent != null
                ? DxpAgentIdentityResolver.ResolveIds(agent)
                : (null, null);

            await _mediator.Publish(new ConversationResolutionAnalyzedDomainEvent(
                tenantName,
                conversation.SessionId,
                conversation.SubjectId,
                tenantAgentId,
                publishedTenantAgentId,
                isResolved,
                analyzedAt,
                conversation.IsTestChat,
                conversation.ConversationDate), cancellationToken);
        }

        internal static string BuildConversationText(ChatHistory chatHistory)
        {
            // US 7502: the "Stopped" divider is not something the assistant said. Bug 8189: an attached file (a document can be
            // millions of characters) is not something the user said either.
            var lines = chatHistory
                .Where(m => !string.IsNullOrWhiteSpace(m.Content) && !StoppedTurnMarker.IsMarker(m) && !ChatHistoryMessageExtractor.IsAttachment(m))
                .Select(m =>
                {
                    if (m.Role != AuthorRole.User)
                        return $"Assistant: {m.Content}";

                    // Annotated with this turn's own MessageId so the model can reference it back in
                    // "messageResolutions" — messages with no MessageId metadata (shouldn't normally happen
                    // for a real user turn) are still included, just without a marker.
                    var messageId = ChatHistoryMessageExtractor.GetMessageIdFromMetadata(m.Metadata);
                    var tag = messageId.HasValue ? $"[MessageId: {messageId}] " : string.Empty;
                    return $"{tag}User: {m.Content}";
                });

            return string.Join("\n", lines);
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
                // If parsing fails, default to resolved
            }

            return true;
        }

        /// <summary>
        /// Parses, persists (per <c>MessageTopicClassification</c> row) and re-reports to DXP the optional
        /// "messageResolutions" breakdown from the same LLM response already used for the conversation-level
        /// verdict. Callers must wrap this in their own try/catch — nothing here should ever be allowed to
        /// affect the already-completed conversation-level analysis. Internal (not private) so it can be
        /// unit-tested directly, without needing a working AI provider for the whole RunAsync flow.
        /// </summary>
        internal async Task PersistMessageResolutionsAsync(string content, Conversation conversation, RunnableAgent agent, string tenantName, ChatHistory chatHistory, CancellationToken cancellationToken)
        {
            var entries = ParseMessageResolutions(content);
            if (entries == null || entries.Count == 0)
                return;

            var (tenantAgentId, publishedTenantAgentId) = agent != null
                ? DxpAgentIdentityResolver.ResolveIds(agent)
                : (null, null);

            foreach (var entry in entries)
            {
                var classification = await _db.MessageTopicClassifications
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.MessageId == entry.MessageId && m.SessionId == conversation.SessionId, cancellationToken);

                // No row for this message (or it belongs to a different session than the one being
                // re-analyzed, which would be an unexpected/hallucinated MessageId) — nothing to update
                // or report.
                if (classification == null)
                    continue;

                // Already judged (by this same backfill on a previous run, or by the near-immediate
                // MessageResolutionWorkflow right after the turn) — never re-report the same resolution a
                // second time; the message stays reported exactly once. This is deliberately NOT bypassed
                // by anything in this activity itself: the only way IsResolved goes back to null for a
                // message already reported is TopicClassificationJobHandler's batch reclassification, which
                // deletes and recreates the MessageTopicClassification row from scratch (new TopicVersion,
                // IsResolved reset) — at that point this same check naturally lets it be judged and reported
                // again, now under the new version.
                if (classification.IsResolved.HasValue)
                    continue;

                var (question, answer, answerMessageId) = ExtractQuestionAndAnswer(chatHistory, entry.MessageId);

                // US 7142 ContactForm corner case: if the only assistant reply found is filler (the
                // real answer hasn't been persisted yet — form not submitted, or the replay hasn't run),
                // there is nothing real to judge. Never persist/report a resolution computed from filler
                // text; leave this message for a later re-analysis to pick up once a real answer exists.
                if (answer == null)
                    continue;

                // !m.IsResolved.HasValue is enforced in the UPDATE predicate itself, not just in the
                // classification.IsResolved.HasValue check above — that check and this UPDATE are not atomic
                // with each other, so MessageResolutionWorkflow's near-immediate path could judge the SAME
                // message in between them. Without this, the backfill could overwrite that verdict and
                // publish a second DXP resolution event for the same message (found via a Copilot PR review).
                var updated = await _db.MessageTopicClassifications
                    .Where(m => m.MessageId == entry.MessageId && m.SessionId == conversation.SessionId && !m.IsResolved.HasValue)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.IsResolved, entry.IsResolved)
                        .SetProperty(m => m.AnswerMessageId, answerMessageId), cancellationToken);

                if (updated == 0)
                    continue;

                try
                {
                    await _mediator.Publish(new MessageResolutionAnalyzedDomainEvent(
                        tenantName,
                        conversation.SessionId,
                        conversation.SubjectId,
                        tenantAgentId,
                        publishedTenantAgentId,
                        entry.MessageId.ToString(),
                        classification.TopicId,
                        classification.TopicName,
                        entry.IsResolved,
                        question,
                        answer,
                        conversation.IsTestChat,
                        classification.MessageDate,
                        conversation.ConversationDate,
                        classification.TopicVersion,
                        answerMessageId?.ToString()), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ConversationAnalysis: failed to report per-message resolution to DXP for message {MessageId}.", entry.MessageId);
                }
            }
        }

        /// <summary>
        /// Parses the optional "messageResolutions" array from the raw LLM response. Returns null on any
        /// malformed/missing input — never throws. Kept entirely separate from <see cref="ParseIsResolved"/>
        /// so a failure here can never be mistaken for (or interfere with) the conversation-level parsing.
        /// </summary>
        internal static List<(Guid MessageId, bool IsResolved)> ParseMessageResolutions(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                var doc = JsonDocument.Parse(content.Trim());
                if (!doc.RootElement.TryGetProperty("messageResolutions", out var arrayProp) || arrayProp.ValueKind != JsonValueKind.Array)
                    return null;

                var results = new List<(Guid, bool)>();
                foreach (var item in arrayProp.EnumerateArray())
                {
                    if (!item.TryGetProperty("messageId", out var idProp) || !item.TryGetProperty("isResolved", out var resolvedProp))
                        continue;

                    if (!Guid.TryParse(idProp.GetString(), out var messageId))
                        continue;

                    results.Add((messageId, resolvedProp.GetBoolean()));
                }

                return results;
            }
            catch
            {
                // Malformed/absent messageResolutions — log and skip, handled by the caller's try/catch.
                return null;
            }
        }

        /// <summary>
        /// Thin forwarder to the shared <see cref="ChatHistoryMessageExtractor"/> (moved there so the
        /// Worker's <c>TopicClassificationJobHandler</c> can reuse the exact same extraction logic) — kept as
        /// its own internal static method here purely so existing tests calling
        /// <c>ConversationAnalysisWorkflowActivity.ExtractQuestionAndAnswer</c> directly don't need to change.
        /// </summary>
        internal static (string question, string answer, Guid? answerMessageId) ExtractQuestionAndAnswer(ChatHistory chatHistory, Guid messageId)
            => ChatHistoryMessageExtractor.ExtractQuestionAndAnswer(chatHistory, messageId);
    }
}
