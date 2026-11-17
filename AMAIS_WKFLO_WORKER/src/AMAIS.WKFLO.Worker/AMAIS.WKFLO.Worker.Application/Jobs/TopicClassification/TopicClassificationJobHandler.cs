using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Worker.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Worker.Application.Jobs.TopicClassification
{
    public class TopicClassificationJobHandler : IJobHandler
    {
        public string JobType => CommonJobTypeConsts.TopicClassification;

        private readonly IMasterDataDbContext _db;
        private readonly IChatStoreService _chatStoreService;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly WorkerSettings _settings;
        private readonly ILogger<TopicClassificationJobHandler> _logger;
        private readonly IDxpChatbotAnalyticsService _dxpChatbotAnalyticsService;

        public TopicClassificationJobHandler(
            IMasterDataDbContext db,
            IChatStoreService chatStoreService,
            IDateTimeProvider dateTimeProvider,
            IOptions<WorkerSettings> settings,
            ILogger<TopicClassificationJobHandler> logger,
            IDxpChatbotAnalyticsService dxpChatbotAnalyticsService)
        {
            _db = db;
            _chatStoreService = chatStoreService;
            _dateTimeProvider = dateTimeProvider;
            _settings = settings.Value;
            _logger = logger;
            _dxpChatbotAnalyticsService = dxpChatbotAnalyticsService;
        }

        public async Task ExecuteAsync(ClassificationJob job, CancellationToken cancellationToken)
        {
            var agent = await _db.Agents.AsNoTracking()
                .Include(a => a.Tenant)
                .Include(a => a.AIModel).ThenInclude(m => m.Secret)
                .Include(a => a.AgentTools).ThenInclude(at => at.Tool)
                .Where(a => a.Id == job.AgentId)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentId} not found for classification job {JobId}", job.AgentId, job.Id);
                return;
            }

            var classificationTool = agent.AgentTools?
                .FirstOrDefault(at => at.Tool != null && !at.Tool.IsDeleted
                    && at.Tool is RunableTool rt && rt.IsActive
                    && at.Tool.Type == CommonToolTypeConsts.ChatClassification)
                ?.Tool;

            if (classificationTool == null)
            {
                _logger.LogWarning("No active ChatClassification tool found for agent {AgentId}, job {JobId}", job.AgentId, job.Id);
                return;
            }

            ChatClassificationToolConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<ChatClassificationToolConfig>(classificationTool.Config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize ChatClassification config for job {JobId}", job.Id);
                return;
            }

            var activeTopics = config?.Topics?.Where(t => t.IsActive).ToList();
            if (activeTopics == null || activeTopics.Count == 0)
            {
                _logger.LogInformation("No active topics for agent {AgentId}, job {JobId} — nothing to classify", job.AgentId, job.Id);
                return;
            }

            var topicVersion = ChatClassificationConfigHelper.ResolveTopicVersion(config);
            var dxpContextTemplate = await BuildDxpContextTemplateAsync(agent, cancellationToken);

            var provider = AIProviderHelper.GetProvider(agent);
            if (provider == null)
            {
                _logger.LogError("Could not resolve AI provider for agent {AgentId}, job {JobId}", job.AgentId, job.Id);
                return;
            }

            var kernel = AIProviderHelper.GetKernel(provider, null, _dateTimeProvider, null);
            if (kernel == null)
            {
                _logger.LogError("Could not build kernel for agent {AgentId}, job {JobId}", job.AgentId, job.Id);
                return;
            }

            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var modelName = agent.AIModel?.Model ?? agent.AIModel?.DeploymentName ?? "unknown";
            var lookbackDate = _dateTimeProvider.DateTimeOffsetUtcNow().AddMonths(-_settings.LookbackMonths);

            int skip = 0;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;
            int totalConversations = 0;
            int totalMessages = 0;

            while (true)
            {
                var conversations = await _db.Conversations.AsNoTracking()
                    .Where(c => c.AgentId == job.AgentId
                        && c.ConversationDate >= lookbackDate)
                    .OrderBy(c => c.Id)
                    .Skip(skip)
                    .Take(_settings.BatchSize)
                    .ToListAsync(cancellationToken);

                if (conversations.Count == 0) break;

                foreach (var conversation in conversations)
                {
                    try
                    {
                        var (tokens1, tokens2, messages) = await ProcessConversationAsync(
                            conversation, agent.Tenant.Name, activeTopics, chatCompletionService, dxpContextTemplate, topicVersion, cancellationToken);
                        totalInputTokens += tokens1;
                        totalOutputTokens += tokens2;
                        totalConversations++;
                        totalMessages += messages;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to classify conversation {SessionId} for job {JobId}", conversation.SessionId, job.Id);
                    }
                }

                skip += conversations.Count;
                if (conversations.Count < _settings.BatchSize) break;
            }

            job.AIModelName = modelName;
            job.TotalInputTokens = totalInputTokens;
            job.TotalOutputTokens = totalOutputTokens;
            job.TotalConversations = totalConversations;
            job.TotalMessages = totalMessages;
        }

        internal async Task<(int inputTokens, int outputTokens, int messages)> ProcessConversationAsync(
            Conversation conversation,
            string tenantName,
            List<ClassificationTopic> activeTopics,
            IChatCompletionService chatCompletionService,
            DxpChatbotEventContext dxpContextTemplate,
            string topicVersion,
            CancellationToken cancellationToken)
        {
            var (_, name, chatHistory, messageRatings) = await _chatStoreService.LoadChatAsync(
                tenantName, conversation.AgentId, conversation.SessionId, conversation.UserId);

            if (chatHistory == null || chatHistory.Count == 0)
                return (0, 0, 0);

            var topicList = string.Join("\n", activeTopics.Select(t =>
                $"- ID: {t.Id} | Name: {t.Name} | Description: {t.Description}"));

            int inputTokens = 0;
            int outputTokens = 0;
            int messagesProcessed = 0;
            bool historyModified = false;
            var classifiedMessages = new List<(Guid MessageId, Guid? TopicId, string TopicName, DateTimeOffset MessageDate, bool? IsResolved, string Question, string Response, Guid? AnswerMessageId, Guid? FeedbackLookupMessageId)>();

            // Reclassifying topics must never affect resolution judgment (US 7142) — a topic taxonomy change
            // has nothing to do with whether a question/answer exchange was resolved, and re-judging that here
            // would require re-analyzing the whole conversation holistically anyway, which is a different
            // concern handled elsewhere (MessageResolutionWorkflow / ConversationAnalysisWorkflowActivity).
            // Existing rows are UPDATED in place (topic identity/version only) instead of being deleted and
            // recreated from scratch, so IsResolved/AnswerMessageId survive untouched across any number of
            // reclassifications — a message already judged stays judged; one never judged stays null.
            var existingClassifications = await _db.MessageTopicClassifications
                .Where(m => m.SessionId == conversation.SessionId && m.AgentId == conversation.AgentId)
                .ToListAsync(cancellationToken);
            var existingByMessageId = existingClassifications.ToDictionary(m => m.MessageId);
            var touchedMessageIds = new HashSet<Guid>();

            void UpsertClassification(Guid msgId, Guid? topicId, string topicName, DateTimeOffset msgDate, MessageTopicClassification existingRow)
            {
                if (existingRow != null)
                {
                    existingRow.TopicId = topicId;
                    existingRow.TopicName = topicName;
                    existingRow.MessageDate = msgDate;
                    existingRow.TopicVersion = topicVersion;
                }
                else
                {
                    _db.MessageTopicClassifications.Add(new MessageTopicClassification
                    {
                        TopicId = topicId,
                        TopicName = topicName,
                        SessionId = conversation.SessionId,
                        MessageId = msgId,
                        AgentId = conversation.AgentId,
                        MessageDate = msgDate,
                        TopicVersion = topicVersion
                    });
                }
            }

            foreach (var message in chatHistory)
            {
                if (message.Role != Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User)
                    continue;

                // Bug 8189: an attached file (a document can be millions of characters) is not a question to classify.
                if (ChatHistoryMessageExtractor.IsAttachment(message))
                    continue;

                if (!message.Metadata.TryGetValue(CommonMetadataConsts.MessageId, out var messageIdObj))
                    continue;

                if (!Guid.TryParse(messageIdObj?.ToString(), out var messageId))
                    continue;

                var userText = message.Content ?? string.Empty;
                if (string.IsNullOrWhiteSpace(userText))
                    continue;

                messagesProcessed++;
                touchedMessageIds.Add(messageId);
                existingByMessageId.TryGetValue(messageId, out var existing);

                // The message's own real send date (from its persisted Timestamp metadata) — never "now"
                // (when this reclassification job happens to run). Falls back to whatever was already
                // recorded if that metadata is somehow missing, and only to "now" as a last resort for a
                // message classified for the very first time with no Timestamp metadata at all.
                var messageDate = ChatHistoryMessageExtractor.GetTimestampFromMetadata(message.Metadata)
                    ?? existing?.MessageDate
                    ?? _dateTimeProvider.DateTimeOffsetUtcNow();

                // Preserve whatever resolution judgment already exists — reclassification must never compute,
                // reset, or invent one. The question/answer TEXT (not the verdict) is re-extracted from the
                // current transcript unconditionally — it's cheap (no LLM call, just a transcript walk) and
                // needed below for feedback correlation regardless of judgment status, but only actually
                // reported as this message's Question/Response if it was already judged.
                bool? preservedIsResolved = existing?.IsResolved;
                Guid? preservedAnswerMessageId = existing?.AnswerMessageId;
                var (extractedQuestionText, extractedAnswerText, extractedAnswerMessageId) = ChatHistoryMessageExtractor.ExtractQuestionAndAnswer(chatHistory, messageId);

                // A rating is always given on the ANSWER's own MessageId, never the question's (see
                // RateMessageAsync / CLAUDE.md's "Rating key" note) — so feedback re-reporting below must look
                // up messageRatings by the answer's id, not this loop variable's own (question) id. Prefer the
                // already-judged/persisted AnswerMessageId; fall back to the freshly extracted one so a rated
                // message can still be found even before its resolution has ever been judged.
                Guid? feedbackLookupMessageId = preservedAnswerMessageId ?? extractedAnswerMessageId;

                string question = null;
                string response = null;
                if (preservedIsResolved.HasValue)
                {
                    question = extractedQuestionText;
                    response = extractedAnswerText;
                }

                var classificationPrompt = $"""
                    You are a message classifier. Your task is to identify which topic best matches the user's message.

                    Topics:
                    {topicList}

                    User message: "{userText}"

                    Instructions:
                    - Review all topics and their descriptions carefully.
                    - Return ONLY the UUID of the single best-matching topic.
                    - If no topic clearly applies to the message, respond with exactly: NONE
                    - Do not include any explanation, punctuation, or extra text in your response.
                    """;

                var history = new ChatHistory();
                history.AddUserMessage(classificationPrompt);

                var results = await chatCompletionService.GetChatMessageContentsAsync(history, cancellationToken: cancellationToken);
                var rawResult = results?.FirstOrDefault()?.Content?.Trim();

                if (results?.FirstOrDefault()?.Metadata?.TryGetValue("Usage", out var usageObj) == true && usageObj is Microsoft.SemanticKernel.TextContent usage)
                {
                    // Token counting is best-effort; different providers expose usage differently
                }

                inputTokens += classificationPrompt.Length / 4; // approximate

                if (string.IsNullOrWhiteSpace(rawResult) || rawResult.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                {
                    // Clear topic metadata from this message
                    if (message.Metadata.ContainsKey(CommonMetadataConsts.TopicName))
                    {
                        var mutableMetadata = new Dictionary<string, object>(message.Metadata);
                        mutableMetadata.Remove(CommonMetadataConsts.TopicName);
                        message.Metadata = mutableMetadata;
                        historyModified = true;
                    }

                    // Persist the "uncategorised" outcome so it's distinguishable from "not yet processed"
                    UpsertClassification(messageId, null, null, messageDate, existing);
                    classifiedMessages.Add((messageId, null, null, messageDate, preservedIsResolved, question, response, preservedAnswerMessageId, feedbackLookupMessageId));
                    continue;
                }

                if (!Guid.TryParse(rawResult, out var topicId))
                {
                    // Unparseable topic ID — persist as uncategorised
                    UpsertClassification(messageId, null, null, messageDate, existing);
                    classifiedMessages.Add((messageId, null, null, messageDate, preservedIsResolved, question, response, preservedAnswerMessageId, feedbackLookupMessageId));
                    continue;
                }

                var matchedTopic = activeTopics.FirstOrDefault(t => t.Id == topicId);
                if (matchedTopic == null)
                {
                    // Unknown/inactive topic ID — persist as uncategorised
                    UpsertClassification(messageId, null, null, messageDate, existing);
                    classifiedMessages.Add((messageId, null, null, messageDate, preservedIsResolved, question, response, preservedAnswerMessageId, feedbackLookupMessageId));
                    continue;
                }

                UpsertClassification(messageId, topicId, matchedTopic.Name, messageDate, existing);
                classifiedMessages.Add((messageId, topicId, matchedTopic.Name, messageDate, preservedIsResolved, question, response, preservedAnswerMessageId, feedbackLookupMessageId));

                var mutableMeta = new Dictionary<string, object>(message.Metadata)
                {
                    [CommonMetadataConsts.TopicName] = matchedTopic.Name
                };
                message.Metadata = mutableMeta;
                historyModified = true;

                outputTokens += (rawResult?.Length ?? 0) / 4;
            }

            // Any existing row whose message no longer qualifies this run (edited/pruned transcript — rare)
            // is stale and removed, same as the previous delete-everything behavior did for it specifically.
            foreach (var kvp in existingByMessageId)
            {
                if (!touchedMessageIds.Contains(kvp.Key))
                    _db.MessageTopicClassifications.Remove(kvp.Value);
            }

            await _db.SaveChangesAsync(cancellationToken);

            if (dxpContextTemplate != null)
            {
                var dxpContext = dxpContextTemplate with { SessionId = conversation.SessionId, SubjectId = conversation.SubjectId, IsTestChat = conversation.IsTestChat };
                foreach (var (messageId, topicId, topicName, msgDate, isResolved, question, response, answerMessageId, _) in classifiedMessages)
                {
                    // Carries the PRESERVED resolution verdict (if any) alongside the new topic identity/
                    // version, so the version-2 document is complete from the start instead of missing
                    // IsResolved/Question/Response until some later, separate re-report.
                    await _dxpChatbotAnalyticsService.ReportMessageTopicAsync(
                        dxpContext, messageId.ToString(), topicId, topicName, topicVersion, msgDate, conversation.ConversationDate,
                        isResolved, question, response, answerMessageId?.ToString());
                }

                // US 7142: re-report feedback under the CURRENT topic/version for every reclassified message
                // that already carries a rating, so Satisfaction rate self-heals within the lookback window
                // the same way Questions/Resolved rate already do above — a rating given under a superseded
                // topic version would otherwise stay stuck forever, since a rating is normally sent to DXP
                // exactly once, at the moment the visitor rates. messageRatings can legitimately be null (a
                // conversation with no ratings at all yet).
                //
                // A rating is always keyed by the ANSWER's own MessageId, never the question's (see
                // RateMessageTenantAgentByIdCommandHandler's own doc comment) — messageRatings must be looked
                // up (and re-reported) by FeedbackLookupMessageId, not this loop variable's own (question) id,
                // otherwise this lookup silently never matches anything real.
                if (messageRatings != null)
                {
                    foreach (var (_, topicId, topicName, msgDate, _, _, _, _, feedbackLookupMessageId) in classifiedMessages)
                    {
                        if (feedbackLookupMessageId == null)
                            continue;

                        if (!messageRatings.TryGetValue(feedbackLookupMessageId.Value.ToString(), out var rating))
                            continue;

                        await _dxpChatbotAnalyticsService.ReportMessageFeedbackAsync(
                            dxpContext, feedbackLookupMessageId.Value.ToString(), rating == "thumbsUp",
                            topicId, topicName, msgDate, conversation.ConversationDate, topicVersion: topicVersion);
                    }
                }
            }

            if (historyModified && name != null)
            {
                await _chatStoreService.SaveChatAsync(
                    tenantName, conversation.AgentId, conversation.SessionId,
                    name, chatHistory, conversation.ConversationDate ?? _dateTimeProvider.DateTimeOffsetUtcNow(),
                    conversation.UserId);
            }

            return (inputTokens, outputTokens, messagesProcessed);
        }

        /// <summary>
        /// Resolves the DXP integration + agent identity once per job run (constant across every conversation
        /// processed for this <paramref name="agent"/>), as a template with an empty <c>SessionId</c>/
        /// <c>SubjectId</c> that <see cref="ProcessConversationAsync"/> fills in per conversation (from the
        /// persisted <c>Conversation.SubjectId</c> column — there's no live <c>ChatContext</c> in this
        /// background job) via a record <c>with</c> expression. Returns null when no active DXP integration is
        /// configured for the tenant — callers must skip reporting entirely in that case.
        /// </summary>
        private async Task<DxpChatbotEventContext> BuildDxpContextTemplateAsync(Agent agent, CancellationToken cancellationToken)
        {
            // OrderByDescending(Id): see ReportConversationStartedDomainEventHandler (Common.AI.Application)
            // for why this ordering exists — makes row selection deterministic if the TenantId/Tenant.Name
            // uniqueness invariant is ever violated, rather than relying on undefined FirstOrDefaultAsync ordering.
            var integration = await _db.DxpIntegrations
                .AsNoTracking()
                .Where(x => x.Tenant.Name == agent.Tenant.Name && x.IsActive && !x.IsDeleted)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (integration == null)
                return null;

            var (tenantAgentId, publishedTenantAgentId) = DxpAgentIdentityResolver.ResolveIds(agent);
            var shareId = await DxpAgentIdentityResolver.ResolveShareIdAsync(_db, tenantAgentId, publishedTenantAgentId, cancellationToken);

            return new DxpChatbotEventContext(
                integration.DxpTenantName,
                integration.Domain,
                integration.ApiKey,
                integration.TenantId,
                agent.Tenant.Name,
                string.Empty,
                string.Empty,
                tenantAgentId,
                publishedTenantAgentId,
                shareId);
        }
    }
}
