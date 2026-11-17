using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    public partial class SemanticKernelAgentService
    {
        /// <summary>
        /// <paramref name="isContactFormReplay"/>: true for wkflo_ui's ContactForm "replay" turn (see
        /// <see cref="Models.RunTextOptions.IsContactFormReplay"/>) — the replayed text is the exact same
        /// question already classified during the original turn, so classifying it again would both fire
        /// a second, redundant "Chatbot Message Topic" analytics event for one visitor question and
        /// double-count it in Top Topics. Short-circuits before touching the kernel, the DB, or the
        /// mediator — this is the one, precise "is this turn a replay" gate for classification, since
        /// classification otherwise fires unconditionally per message with no history-length check.
        /// </summary>
        internal async Task<(string topicName, string handoffMessage)> RunChatClassificationAsync(Kernel kernel, MD.Domain.Entities.Agent agent, string message, Guid userMessageId, string sessionId, bool isContactFormReplay, CancellationToken cancellationToken)
        {
            if (isContactFormReplay)
                return (null, null);

            var classificationTool = agent.AgentTools?
                .FirstOrDefault(at =>
                    at.Tool != null
                    && !at.Tool.IsDeleted
                    && at.Tool is RunableTool rt && rt.IsActive
                    && at.Tool.Type == CommonToolTypeConsts.ChatClassification)
                ?.Tool;

            if (classificationTool == null)
                return (null, null);

            ChatClassificationToolConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<ChatClassificationToolConfig>(classificationTool.Config);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize ChatClassification config for tool {ToolId}", classificationTool.Id);
                return (null, null);
            }

            var activeTopics = config?.Topics?.Where(t => t.IsActive).ToList();

            if (activeTopics == null || activeTopics.Count == 0)
                return (null, null);

            var topicVersion = ChatClassificationConfigHelper.ResolveTopicVersion(config);

            try
            {
                var topicList = string.Join("\n", activeTopics.Select(t =>
                    $"- ID: {t.Id} | Name: {t.Name} | Description: {t.Description}"));

                var classificationPrompt = $"""
                    You are a message classifier. Your task is to identify which topic best matches the user's message.

                    Topics:
                    {topicList}

                    User message: "{message}"

                    Instructions:
                    - Review all topics and their descriptions carefully.
                    - Return ONLY the UUID of the single best-matching topic.
                    - If no topic clearly applies to the message, respond with exactly: NONE
                    - Do not include any explanation, punctuation, or extra text in your response.
                    """;

                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
                var classificationHistory = new ChatHistory();
                classificationHistory.AddUserMessage(classificationPrompt);

                var result = await chatCompletionService.GetChatMessageContentsAsync(classificationHistory, cancellationToken: cancellationToken);
                var rawResult = result?.FirstOrDefault()?.Content?.Trim();

                if (string.IsNullOrWhiteSpace(rawResult) || rawResult.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                {
                    await PersistClassificationOutcomeAsync(null, null, sessionId, userMessageId, agent, topicVersion, cancellationToken);
                    return (null, null);
                }

                if (!Guid.TryParse(rawResult, out var topicId))
                {
                    _logger.LogWarning("ChatClassification returned unexpected value: {Value}", rawResult);
                    await PersistClassificationOutcomeAsync(null, null, sessionId, userMessageId, agent, topicVersion, cancellationToken);
                    return (null, null);
                }

                var matchedTopic = activeTopics.FirstOrDefault(t => t.Id == topicId);
                if (matchedTopic == null)
                {
                    _logger.LogWarning("ChatClassification returned unknown topic ID: {TopicId}", topicId);
                    await PersistClassificationOutcomeAsync(null, null, sessionId, userMessageId, agent, topicVersion, cancellationToken);
                    return (null, null);
                }

                await PersistClassificationOutcomeAsync(topicId, matchedTopic.Name, sessionId, userMessageId, agent, topicVersion, cancellationToken);

                return (matchedTopic.Name, matchedTopic.HandoffMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatClassification failed for session {SessionId}, message {MessageId}", sessionId, userMessageId);
                return (null, null);
            }
        }

        /// <summary>
        /// Persists the classification outcome (match or "uncategorised") for a message, exactly once per message.
        /// Deliberately does NOT report anything to DXP at this point — the answer doesn't exist yet, and product
        /// direction is that a message must produce exactly ONE "Chatbot Message Topic" analytics event, sent once
        /// the resolution judgment is known (see <see cref="MessageResolutionWorkflowActivity"/>, which reads
        /// TopicId/TopicName/TopicVersion back from this same row and reports everything together). Reporting here
        /// too used to create a second, premature document for the same message a few seconds before the real one,
        /// which only complicated the "latest wins" aggregation downstream for no benefit. Unlike the batch job,
        /// this real-time path has no delete-first guard, so a duplicate call for the same message (e.g. a retried
        /// request) must not throw or create a second row now that MessageId is unique.
        /// </summary>
        internal async Task PersistClassificationOutcomeAsync(Guid? topicId, string topicName, string sessionId, Guid messageId, MD.Domain.Entities.Agent agent, string topicVersion, CancellationToken cancellationToken)
        {
            try
            {
                var alreadyClassified = await _db.MessageTopicClassifications
                    .AsNoTracking()
                    .AnyAsync(m => m.MessageId == messageId, cancellationToken);

                if (alreadyClassified)
                {
                    _logger.LogInformation("Classification outcome already persisted for message {MessageId}; skipping duplicate insert", messageId);
                    return;
                }

                _db.MessageTopicClassifications.Add(new MessageTopicClassification
                {
                    TopicId = topicId,
                    TopicName = topicName,
                    SessionId = sessionId,
                    MessageId = messageId,
                    AgentId = agent.Id,
                    MessageDate = _dateTimeProvider.DateTimeOffsetUtcNow(),
                    TopicVersion = topicVersion
                });

                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Another concurrent call already inserted the row for this message (unique MessageId constraint) — treat as a no-op.
                _logger.LogWarning(ex, "Concurrent classification insert detected for message {MessageId}; treating as already persisted", messageId);
            }
        }
    }
}
