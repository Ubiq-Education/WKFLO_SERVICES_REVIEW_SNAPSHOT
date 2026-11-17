using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ConversationStarted;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Helpers;
using AMAIS.WKFLO.Common.Mediator;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated
{
    public class UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler : INotificationHandler<ChatHistoryUpdatedDomainEvent>
    {
        // Conversation.Name and Conversation.PreviewLine are both NVARCHAR(300).
        private const int ConversationTextMaxLength = 300;

        // Enough of the first exchange for the title prompt (ConversationTitleWorkflowActivity cuts it further).
        private const int TitleInputMaxLength = 2000;

        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;
        private readonly ChatContext _chatContext;
        private readonly IMasterDataDbContext _db;
        private readonly IConversationAnalysisWorkflowScheduler _workflowScheduler;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IWkfloMediator _mediator;
        private readonly IConversationTitleWorkflowScheduler _titleWorkflowScheduler;

        public UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler(IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext,
            ChatContext chatContext, IMasterDataDbContext db, IConversationAnalysisWorkflowScheduler workflowScheduler, IDateTimeProvider dateTimeProvider,
            IWkfloMediator mediator, IConversationTitleWorkflowScheduler titleWorkflowScheduler)
        {
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _chatContext = chatContext;
            _db = db;
            _workflowScheduler = workflowScheduler;
            _dateTimeProvider = dateTimeProvider;
            _mediator = mediator;
            _titleWorkflowScheduler = titleWorkflowScheduler;
        }

        public async ValueTask Handle(ChatHistoryUpdatedDomainEvent notification, CancellationToken cancellationToken)
        {
            // Only the STM blob persistence itself (and the ChatName it derives) is genuinely STM-specific.
            // The Conversation row bookkeeping and analysis-workflow scheduling below must happen regardless
            // of EnableSTM: ConversationAnalysisWorkflowActivity.RunAsync loads chat history straight from
            // blob storage (IChatStoreService), never from anything STM-specific, so gating it on EnableSTM
            // was unintentional coupling — it made STM-disabled agents permanently unanalyzable.
            int? thumbsUp = null;
            int? thumbsDown = null;

            // Loaded before the blob save (not after) so that a generated AI title can be written to the blob below.
            var existing = await _db.Conversations
                .FirstOrDefaultAsync(c => c.AgentId == notification.RunnableAgent.Id && c.SessionId == notification.SessionId, cancellationToken);

            // US 7507: once the conversation has its AI title (ConversationTitleWorkflowActivity), that title is the
            // chat name. ChatName was read from the blob when this turn started, so it can still be the old name.
            // Bug 7958: a soft-deleted row is revived as a new conversation, so its old title must not carry over.
            if (existing is { IsDeleted: false, TitleStatus: ConversationTitleStatus.Generated } && !string.IsNullOrEmpty(existing.Name))
                notification.ChatName = existing.Name;

            if (notification.RunnableAgent.EnableSTM)
            {
                if (string.IsNullOrEmpty(notification.ChatName))
                    notification.ChatName = GetFirstUserMessagePreview(notification.ChatHistory);

                var (savedThumbsUp, savedThumbsDown) = await _chatStoreService.SaveChatAsync(_tenantContext.TenantName, notification.RunnableAgent.Id, notification.SessionId, notification.ChatName, notification.ChatHistory, notification.CreatedAt, _userContext.Guid);
                thumbsUp = savedThumbsUp;
                thumbsDown = savedThumbsDown;
            }

            var (conversationSynced, titleInput) = await SyncConversationAsync(notification, existing, thumbsUp, thumbsDown, cancellationToken);

            // US 7502: a stopped turn adds no content: nothing new to analyse.
            if (conversationSynced && !notification.IsStoppedTurn)
                await ScheduleAnalysisWorkflowAsync(notification, cancellationToken);

            if (titleInput != null)
                await ScheduleTitleWorkflowAsync(titleInput);
        }

        private async Task<(bool synced, ConversationTitleWorkflowInput titleInput)> SyncConversationAsync(ChatHistoryUpdatedDomainEvent notification, Conversation existing,
            int? thumbsUp, int? thumbsDown, CancellationToken cancellationToken)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Cacheable()
                .FirstOrDefaultAsync(t => t.Name == _tenantContext.TenantName, cancellationToken);

            var userId = string.IsNullOrEmpty(_userContext.Guid) ? Guid.Empty.ToString() : _userContext.Guid;

            if (tenant == null)
                return (false, null);

            // Bug 7958: (AgentId, SessionId) is the conversation's identity, so a soft-deleted row (DeleteTenantAgentChat /
            // WipeShortTermMemoryTenantAgent) still matches when the client keeps chatting on the same session id. Updating
            // it in place would land every new message in a row Chat Logs never shows. Instead the deleted row is revived
            // in place (no duplicate (AgentId, SessionId) row) as a brand-new conversation: none of its old per-conversation
            // state survives, and it counts as new so ConversationStartedDomainEvent is published for it.
            var isRevivedConversation = existing is { IsDeleted: true };
            var isNewConversation = existing == null || isRevivedConversation;

            // US 7507: unlike Name (which becomes the AI-generated title), PreviewLine is the first user message
            // and is never overwritten once set. Computed regardless of EnableSTM: it only needs the turn's history.
            var previewLine = GetFirstUserMessagePreview(notification.ChatHistory);

            Conversation conversation;

            if (existing == null)
            {
                conversation = new Conversation
                {
                    AgentId = notification.RunnableAgent.Id,
                    TenantId = tenant.Id,
                    SessionId = notification.SessionId,
                    Name = notification.ChatName,
                    PreviewLine = previewLine,
                    ConversationDate = notification.CreatedAt,
                    UserId = userId,
                    SubjectId = _chatContext.SubjectId,
                    TotalThumbsUp = thumbsUp ?? 0,
                    TotalThumbsDown = thumbsDown ?? 0,
                    LastActivityAt = _dateTimeProvider.DateTimeOffsetUtcNow(),
                    AnalysisStatus = ConversationAnalysisStatus.Pending,
                    IsTestChat = _chatContext.IsTestChat,
                    CreatedOn = _dateTimeProvider.DateTimeUtcNow()
                };
                await _db.Conversations.AddAsync(conversation, cancellationToken);
            }
            else if (isRevivedConversation)
            {
                conversation = existing;

                existing.IsDeleted = false;
                existing.Name = notification.ChatName;
                existing.PreviewLine = previewLine;
                // US 7507: the deleted conversation's AI title belongs to it, not to the new one: decide the title again.
                existing.TitleStatus = ConversationTitleStatus.Pending;
                existing.ConversationDate = notification.CreatedAt;
                existing.UserId = userId;
                existing.SubjectId = _chatContext.SubjectId;
                existing.TotalThumbsUp = thumbsUp ?? 0;
                existing.TotalThumbsDown = thumbsDown ?? 0;
                existing.LastActivityAt = _dateTimeProvider.DateTimeOffsetUtcNow();
                existing.IsResolved = true;
                // US 7502: same rule as the update branch below, a stopped turn schedules no analysis, so it must not
                // leave the revived conversation waiting in Pending for an analysis that is not coming.
                if (!notification.IsStoppedTurn)
                    existing.AnalysisStatus = ConversationAnalysisStatus.Pending;
                // The deleted conversation's test-chat flag says nothing about the new one: take the current value as-is.
                existing.IsTestChat = _chatContext.IsTestChat;
                existing.LastUpdatedOn = _dateTimeProvider.DateTimeUtcNow();
            }
            else
            {
                conversation = existing;

                // US 7507: a generated AI title is never replaced by the per-turn chat name.
                if (!string.IsNullOrEmpty(notification.ChatName) && existing.TitleStatus != ConversationTitleStatus.Generated)
                    existing.Name = notification.ChatName;

                if (string.IsNullOrEmpty(existing.PreviewLine) && !string.IsNullOrEmpty(previewLine))
                    existing.PreviewLine = previewLine;

                if (!string.IsNullOrEmpty(userId))
                    existing.UserId = userId;

                if (!string.IsNullOrEmpty(_chatContext.SubjectId))
                    existing.SubjectId = _chatContext.SubjectId;

                // Once true, IsTestChat must never revert to false on a later sync.
                if (_chatContext.IsTestChat)
                    existing.IsTestChat = true;

                // Rating counts only come from the STM blob (SaveChatAsync); when STM is disabled there's no
                // fresh count to report, so leave whatever is already persisted untouched instead of zeroing it.
                if (thumbsUp.HasValue)
                    existing.TotalThumbsUp = thumbsUp.Value;

                if (thumbsDown.HasValue)
                    existing.TotalThumbsDown = thumbsDown.Value;

                existing.LastActivityAt = _dateTimeProvider.DateTimeOffsetUtcNow();

                // US 7502: a stopped turn adds no content and schedules no analysis, so it leaves the status as it was
                // (it never leaves a conversation waiting in Pending for an analysis that is not coming).
                if (!notification.IsStoppedTurn)
                    existing.AnalysisStatus = ConversationAnalysisStatus.Pending;
                existing.LastUpdatedOn = _dateTimeProvider.DateTimeUtcNow();
            }

            var titleInput = await PrepareTitleGenerationAsync(conversation, notification, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            // Publish immediately after the insert is committed, from the ONE place that knows with certainty
            // (no ambiguity, no dependency on message shape or on any other handler's dispatch order) that this
            // Conversation row is genuinely new (inserted, or a deleted row revived as a new conversation). See
            // ConversationStartedDomainEvent's own doc comment.
            if (isNewConversation)
                await _mediator.Publish(new ConversationStartedDomainEvent(notification.RunnableAgent, notification.SessionId, notification.CreatedAt), cancellationToken);

            return (true, titleInput);
        }

        /// <summary>
        /// US 7507: decides, once per conversation, whether it gets an AI title. Only MAISY conversations do (not test
        /// chats, not the rows of the agents MAISY spins up), and only after the first completed exchange: a user message
        /// and an answer to it. Returns the workflow input when the title should be generated now; marks the conversation
        /// Skipped when it never will be, so the Maisy check runs at most once per conversation.
        /// </summary>
        private async Task<ConversationTitleWorkflowInput> PrepareTitleGenerationAsync(Conversation conversation, ChatHistoryUpdatedDomainEvent notification, CancellationToken cancellationToken)
        {
            if (conversation.TitleStatus != ConversationTitleStatus.Pending || notification.IsStoppedTurn)
                return null;

            var (firstUserMessage, firstAssistantMessage) = GetFirstExchange(notification.ChatHistory);
            if (firstUserMessage == null || firstAssistantMessage == null)
                return null;

            if (conversation.IsTestChat || !await AgentTagHelper.IsMaisyAgentAsync(_db, notification.RunnableAgent.Id, cancellationToken))
            {
                conversation.TitleStatus = ConversationTitleStatus.Skipped;
                return null;
            }

            return new ConversationTitleWorkflowInput
            {
                TenantName = _tenantContext.TenantName,
                AgentId = notification.RunnableAgent.Id,
                SessionId = notification.SessionId,
                FirstUserMessage = Cut(firstUserMessage, TitleInputMaxLength),
                FirstAssistantMessage = Cut(firstAssistantMessage, TitleInputMaxLength)
            };
        }

        private static (string user, string assistant) GetFirstExchange(ChatHistory chatHistory)
        {
            // The typed text of the first question, not an attachment added before it (see GetFirstQuestionText).
            var question = GetFirstQuestionText(chatHistory);
            if (question == null)
                return (null, null);

            var answer = chatHistory
                .SkipWhile(m => m.Role != AuthorRole.User)
                .FirstOrDefault(m => m.Role == AuthorRole.Assistant && !string.IsNullOrWhiteSpace(m.Content) && !StoppedTurnMarker.IsMarker(m));

            return (question, answer?.Content);
        }

        private static string Cut(string value, int maxLength) =>
            value.Length > maxLength ? value[..maxLength] : value;

        private async Task ScheduleTitleWorkflowAsync(ConversationTitleWorkflowInput input)
        {
            try
            {
                var instanceId = $"conv-title-{input.SessionId}-{input.AgentId}-{_dateTimeProvider.DateTimeOffsetUtcNow().ToUnixTimeMilliseconds()}";
                await _titleWorkflowScheduler.ScheduleAsync(instanceId, input);
            }
            catch (Exception)
            {
                // Swallow: the title is a nice-to-have and must never break the chat. The conversation stays Pending,
                // so the next completed turn tries to schedule it again.
            }
        }

        /// <summary>
        /// The text the user typed in their first message. A turn adds each image/document attachment as its own
        /// <see cref="AuthorRole.User"/> entry BEFORE the typed text (see SemanticKernelAgentServiceRun), so the first
        /// user entry may be an attachment ("FileName: ..." / "Document content: ..."). This takes the first run of
        /// consecutive user entries (the first question) and returns its last non-empty entry that is not an
        /// attachment, i.e. the typed text. A first question made only of attachments gives null.
        /// </summary>
        private static string GetFirstUserMessagePreview(ChatHistory chatHistory)
        {
            var firstUserMessage = GetFirstQuestionText(chatHistory);

            if (string.IsNullOrEmpty(firstUserMessage))
                return null;

            return firstUserMessage.Length > ConversationTextMaxLength
                ? firstUserMessage[..ConversationTextMaxLength]
                : firstUserMessage;
        }

        private static string GetFirstQuestionText(ChatHistory chatHistory) =>
            chatHistory?
                .SkipWhile(m => m.Role != AuthorRole.User)
                .TakeWhile(m => m.Role == AuthorRole.User)
                .LastOrDefault(m => !IsAttachment(m) && !string.IsNullOrWhiteSpace(m.Content))?.Content;

        // Image attachments carry the file name on the message metadata, document attachments on their text item.
        private static bool IsAttachment(ChatMessageContent message) =>
            message.Metadata?.ContainsKey(CommonMetadataConsts.FileName) == true
            || message.Items.Any(i => i.Metadata?.ContainsKey(CommonMetadataConsts.FileName) == true);

        private async Task ScheduleAnalysisWorkflowAsync(ChatHistoryUpdatedDomainEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                var instanceId = $"conv-analysis-{notification.SessionId}-{_dateTimeProvider.DateTimeOffsetUtcNow().ToUnixTimeMilliseconds()}";
                var input = new ConversationAnalysisWorkflowInput
                {
                    TenantName = _tenantContext.TenantName,
                    AgentId = notification.RunnableAgent.Id,
                    SessionId = notification.SessionId,
                    UserId = _userContext.Guid
                };

                await _workflowScheduler.ScheduleAsync(instanceId, input);
            }
            catch (Exception)
            {
                // Swallow — analysis scheduling failure must not break the main chat flow
            }
        }
    }
}
