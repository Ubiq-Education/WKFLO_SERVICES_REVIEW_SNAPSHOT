using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Dapr.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows
{
    /// <summary>
    /// US 7507: writes a short AI-generated title onto a MAISY conversation, using MAISY's own model.
    /// <para>
    /// Runs once per conversation: the row is claimed atomically (TitleStatus Pending to Processing) and ends as
    /// Generated or Failed, never Pending again. Never throws: a failure only means the conversation keeps its
    /// first user message as its title.
    /// </para>
    /// <para>
    /// Only Conversation.Name is written here. The chat blob's name is brought in line by the next turn's save
    /// (UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler copies a Generated title onto it), because a
    /// blob read-modify-write from here could race a turn saving the same blob and drop that turn's messages.
    /// </para>
    /// </summary>
    public class ConversationTitleWorkflowActivity : WorkflowActivity<ConversationTitleWorkflowInput, bool>
    {
        internal const int MaxTitleLength = 80;
        internal const int MaxPromptMessageLength = 1000;

        internal const string TitleSystemPrompt =
            "You write titles for chat conversations. " +
            "Given the first message of a conversation and the reply to it, write a short title that says what the conversation is about. " +
            "Use at most 6 words, in the same language as the user's message. " +
            "Reply with the title only: no quotes, no trailing punctuation, no prefix such as \"Title:\".";

        private readonly IMasterDataDbContext _db;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<ConversationTitleWorkflowActivity> _logger;

        public ConversationTitleWorkflowActivity(
            IMasterDataDbContext db,
            IChatNotificationService notificationService,
            ChatContext chatContext,
            IDateTimeProvider dateTimeProvider,
            ILogger<ConversationTitleWorkflowActivity> logger)
        {
            _db = db;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, ConversationTitleWorkflowInput input)
        {
            if (input == null)
            {
                _logger.LogWarning("ConversationTitle: no input, skipping.");
                return true;
            }

            // Only a row this run claimed may be marked Failed: if the claim itself fails, the row is not ours
            // (still Pending, or Processing under another run) and must be left as it is.
            var claimed = false;

            try
            {
                claimed = await _db.Conversations
                    .Where(c => c.SessionId == input.SessionId
                             && c.AgentId == input.AgentId
                             && c.TitleStatus == ConversationTitleStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.TitleStatus, ConversationTitleStatus.Processing)) > 0;

                if (!claimed)
                {
                    _logger.LogInformation("ConversationTitle: session {SessionId} already claimed or not pending, skipping.", input.SessionId);
                    return true;
                }

                var title = await GenerateTitleAsync(input);

                if (title == null)
                {
                    await SetStatusAsync(input, ConversationTitleStatus.Failed);
                    return true;
                }

                await _db.Conversations
                    .Where(c => c.SessionId == input.SessionId && c.AgentId == input.AgentId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.Name, title)
                        .SetProperty(c => c.TitleStatus, ConversationTitleStatus.Generated)
                        .SetProperty(c => c.LastUpdatedOn, (DateTimeOffset?)_dateTimeProvider.DateTimeOffsetUtcNow()));

                await _notificationService.SendConversationTitleUpdatedAsync(input.SessionId, title);

                _logger.LogInformation("ConversationTitle: session {SessionId} titled.", input.SessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConversationTitle: failed for session {SessionId}.", input.SessionId);

                if (!claimed)
                    return true;

                try
                {
                    await SetStatusAsync(input, ConversationTitleStatus.Failed);
                }
                catch (Exception statusEx)
                {
                    _logger.LogError(statusEx, "ConversationTitle: failed to mark session {SessionId} as failed.", input.SessionId);
                }

                return true;
            }
        }

        private async Task<string> GenerateTitleAsync(ConversationTitleWorkflowInput input)
        {
            if (string.IsNullOrWhiteSpace(input.FirstUserMessage))
                return null;

            var agent = (RunnableAgent)await _db.TenantAgents
                .AsNoTracking()
                .Include(a => a.AIModel).ThenInclude(m => m.Secret)
                .FirstOrDefaultAsync(a => a.Id == input.AgentId)
                ?? await _db.PublishedTenantAgents
                .AsNoTracking()
                .Include(a => a.AIModel).ThenInclude(m => m.Secret)
                .FirstOrDefaultAsync(a => a.Id == input.AgentId);

            if (agent == null || !agent.IsActive)
                return null;

            var chatCompletionService = CreateChatCompletionService(agent);
            if (chatCompletionService == null)
                return null;

            var response = await chatCompletionService.GetChatMessageContentAsync(BuildTitlePrompt(input.FirstUserMessage, input.FirstAssistantMessage));

            return SanitizeTitle(response?.Content);
        }

        /// <summary>
        /// MAISY's own model, the same way ConversationAnalysisWorkflowActivity builds it. Returns null when the agent
        /// has no usable model. Virtual so tests can supply a fake model.
        /// </summary>
        internal virtual IChatCompletionService CreateChatCompletionService(RunnableAgent agent)
        {
            var provider = AIProviderHelper.GetProvider(agent);
            if (provider == null)
                return null;

            var kernel = AIProviderHelper.GetKernel(provider, _notificationService, _dateTimeProvider, _chatContext);
            return kernel.GetRequiredService<IChatCompletionService>();
        }

        private Task<int> SetStatusAsync(ConversationTitleWorkflowInput input, ConversationTitleStatus status) =>
            _db.Conversations
                .Where(c => c.SessionId == input.SessionId && c.AgentId == input.AgentId && c.TitleStatus == ConversationTitleStatus.Processing)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.TitleStatus, status));

        internal static ChatHistory BuildTitlePrompt(string firstUserMessage, string firstAssistantMessage)
        {
            var prompt = new ChatHistory();
            prompt.AddSystemMessage(TitleSystemPrompt);

            var text = $"User: {Cut(firstUserMessage, MaxPromptMessageLength)}";
            if (!string.IsNullOrWhiteSpace(firstAssistantMessage))
                text += $"\nAssistant: {Cut(firstAssistantMessage, MaxPromptMessageLength)}";

            prompt.AddUserMessage(text);
            return prompt;
        }

        /// <summary>
        /// Keeps the first non-empty line, strips a "Title:" prefix, wrapping quotes and trailing punctuation, and caps
        /// the length. Returns null when nothing usable is left.
        /// </summary>
        internal static string SanitizeTitle(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var title = content
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;

            if (title.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                title = title["title:".Length..].Trim();

            title = title.Trim('"', '\'', '*', '`', '“', '”', '‘', '’', ' ')
                .TrimEnd('.', '!', '?', ':', ';', ',', ' ');

            if (title.Length > MaxTitleLength)
                title = title[..MaxTitleLength].TrimEnd();

            return string.IsNullOrWhiteSpace(title) ? null : title;
        }

        private static string Cut(string value, int maxLength) =>
            value.Length > maxLength ? value[..maxLength] : value;
    }
}
