using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation
{
    public class SyncConversationCommandHandler : IRequestHandler<SyncConversationCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IDateTimeProvider _dateTimeProvider;

        public SyncConversationCommandHandler(IMasterDataDbContext db, IDateTimeProvider dateTimeProvider)
        {
            _db = db;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<Result> Handle(SyncConversationCommand command, CancellationToken cancellationToken)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Cacheable()
                .FirstOrDefaultAsync(t => t.Name == command.TenantName, cancellationToken);

            if (tenant == null)
                return new NotFoundResult();

            var existing = await _db.Conversations
                .FirstOrDefaultAsync(c => c.AgentId == command.AgentId && c.SessionId == command.SessionId, cancellationToken);

            if (existing == null)
            {
                await _db.Conversations.AddAsync(new Conversation
                {
                    AgentId = command.AgentId,
                    TenantId = tenant.Id,
                    SessionId = command.SessionId,
                    Name = command.Name,
                    ConversationDate = command.ConversationDate ?? _dateTimeProvider.DateTimeOffsetUtcNow(),
                    UserId = command.UserId,
                    SubjectId = command.SubjectId,
                    TotalThumbsUp = command.TotalThumbsUp,
                    TotalThumbsDown = command.TotalThumbsDown,
                    IsTestChat = command.IsTestChat,
                    CreatedOn = _dateTimeProvider.DateTimeOffsetUtcNow()
                }, cancellationToken);
            }
            else if (existing.IsDeleted)
            {
                // Bug 7958: a soft-deleted row still matches (AgentId, SessionId) when the client keeps the same session id.
                // Revive it in place (no duplicate row) as a brand-new conversation instead of updating an invisible row:
                // none of the deleted conversation's per-conversation state survives.
                existing.IsDeleted = false;
                existing.Name = command.Name;
                // US 7507: the deleted conversation's preview and AI title belong to it, not to the new one.
                existing.PreviewLine = null;
                existing.TitleStatus = ConversationTitleStatus.Pending;
                existing.ConversationDate = command.ConversationDate ?? _dateTimeProvider.DateTimeOffsetUtcNow();
                existing.UserId = command.UserId;
                existing.SubjectId = command.SubjectId;
                existing.TotalThumbsUp = command.TotalThumbsUp;
                existing.TotalThumbsDown = command.TotalThumbsDown;
                existing.LastActivityAt = null;
                existing.IsResolved = true;
                existing.AnalysisStatus = ConversationAnalysisStatus.Pending;
                // The deleted conversation's test-chat flag says nothing about the new one: take the current value as-is.
                existing.IsTestChat = command.IsTestChat;
                existing.LastUpdatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
            }
            else
            {
                if (!string.IsNullOrEmpty(command.Name))
                    existing.Name = command.Name;

                if (!string.IsNullOrEmpty(command.UserId))
                    existing.UserId = command.UserId;

                if (!string.IsNullOrEmpty(command.SubjectId))
                    existing.SubjectId = command.SubjectId;

                // Once true, IsTestChat must never revert to false on a later sync.
                if (command.IsTestChat)
                    existing.IsTestChat = true;

                existing.TotalThumbsUp = command.TotalThumbsUp;
                existing.TotalThumbsDown = command.TotalThumbsDown;
                existing.LastUpdatedOn = _dateTimeProvider.DateTimeOffsetUtcNow();
            }

            await _db.SaveChangesAsync(cancellationToken);
            return new SuccessResult<bool>(true);
        }
    }
}
