using AMAIS.WKFLO.AI.Application.Agents.Events.MessageFeedbackRated;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.RateMessageTenantAgentById
{
    public class RateMessageTenantAgentByIdCommandHandler : IRequestHandler<RateMessageTenantAgentByIdCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly IWkfloMediator _mediator;

        public RateMessageTenantAgentByIdCommandHandler(IMasterDataDbContext db, IChatStoreService chatStoreService,
            TenantContext tenantContext, ChatContext chatContext, UserContext userContext, IWkfloMediator mediator)
        {
            _db = db;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _mediator = mediator;
        }

        public async ValueTask<Result> Handle(RateMessageTenantAgentByIdCommand command, CancellationToken cancellationToken)
        {
            var agent = await _db.TenantAgents
                .Where(m => m.Id == command.Id && !m.IsDeleted && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent == null) return new NotFoundResult();

            var (found, thumbsUp, thumbsDown) = await _chatStoreService.RateMessageAsync(
                _tenantContext.TenantName, agent.Id, command.SessionId, _userContext.Guid,
                command.Input.MessageId, command.Input.IsThumbsUp);

            if (!found) return new NotFoundResult();

            var conversation = await _db.Conversations
                .FirstOrDefaultAsync(c => c.AgentId == agent.Id && c.SessionId == command.SessionId, cancellationToken);

            if (conversation != null)
            {
                conversation.TotalThumbsUp = thumbsUp;
                conversation.TotalThumbsDown = thumbsDown;
                await _db.SaveChangesAsync(cancellationToken);
            }

            // A rating is given on the assistant's ANSWER (command.Input.MessageId), but MessageTopicClassification
            // is always keyed by MessageId == the corresponding USER question. AnswerMessageId is the column that
            // exists specifically so this lookup can match the rated message directly - set once the resolution
            // judgment runs (MessageResolutionWorkflow or ConversationAnalysisWorkflowActivity's per-message
            // breakdown), since that's the first point the answer's own MessageId is known. A miss here is
            // legitimate - classification/resolution may never have run for this message yet (same as the
            // "Uncategorized" handling on the Message Topic event itself) - and must not throw or block the
            // rating from being reported. Scoped to this agent/session (not just AnswerMessageId) so an
            // arbitrary/guessed MessageId can't pull another conversation's - potentially another tenant's -
            // TopicId/TopicName/MessageDate into this event. Skipped entirely for test chats, since SendAsync
            // drops those events anyway - no point spending the lookup.
            var isTestChat = conversation?.IsTestChat ?? false;
            var messageIdIsGuid = Guid.TryParse(command.Input.MessageId, out var messageIdGuid);

            var classification = !isTestChat && messageIdIsGuid
                ? await _db.MessageTopicClassifications
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.AnswerMessageId == messageIdGuid && m.AgentId == agent.Id && m.SessionId == command.SessionId, cancellationToken)
                : null;

            // Fallback for a real race (Copilot PR review finding): MessageResolutionWorkflow persists
            // AnswerMessageId asynchronously, AFTER the response has already been returned to the visitor -
            // a fast rating can land before that write completes, missing the lookup above even though the
            // classification row already exists. The question's own MessageId (this row's actual primary
            // key) is set synchronously at classification time, so walking the persisted chat history
            // backward from the rated answer to find its question is never subject to that race.
            if (classification == null && !isTestChat && messageIdIsGuid)
            {
                var (_, _, chatHistory, _) = await _chatStoreService.LoadChatAsync(_tenantContext.TenantName, agent.Id, command.SessionId, _userContext.Guid);
                var questionMessageId = ChatHistoryMessageExtractor.FindQuestionMessageIdForAnswer(chatHistory, messageIdGuid);
                if (questionMessageId.HasValue)
                {
                    classification = await _db.MessageTopicClassifications
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.MessageId == questionMessageId.Value && m.AgentId == agent.Id && m.SessionId == command.SessionId, cancellationToken);
                }
            }

            await _mediator.Publish(new MessageFeedbackRatedDomainEvent(
                _tenantContext.TenantName,
                command.SessionId,
                _chatContext.SubjectId,
                agent.Id,
                agent.PublishedTenantAgentId,
                command.Input.MessageId,
                command.Input.IsThumbsUp,
                conversation?.IsTestChat ?? false,
                classification?.TopicId,
                classification?.TopicName,
                classification?.MessageDate,
                conversation?.ConversationDate,
                classification?.TopicVersion), cancellationToken);

            return new SuccessResult<bool>(true);
        }
    }
}
