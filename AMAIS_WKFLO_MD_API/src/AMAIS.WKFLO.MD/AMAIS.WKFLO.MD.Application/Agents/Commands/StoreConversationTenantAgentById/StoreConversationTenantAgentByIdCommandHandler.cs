using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation;
using AMAIS.WKFLO.MD.Application.Common;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.ChatCompletion;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.StoreConversationTenantAgentById
{
    public class StoreConversationTenantAgentByIdCommandHandler : IRequestHandler<StoreConversationTenantAgentByIdCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly IWkfloMediator _mediator;
        private readonly IDateTimeProvider _dateTimeProvider;

        public StoreConversationTenantAgentByIdCommandHandler(IMasterDataDbContext db, IChatStoreService chatStoreService, TenantContext tenantContext, ChatContext chatContext, IWkfloMediator mediator, IDateTimeProvider dateTimeProvider)
        {
            _db = db;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _mediator = mediator;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<Result> Handle(StoreConversationTenantAgentByIdCommand command, CancellationToken cancellationToken)
        {
            var agent = await _db.PublishedTenantAgents
                .AsNoTracking()
                .Cacheable()
                .FirstOrDefaultAsync(a => a.Id == command.Id && !a.IsDeleted && a.IsActive && a.Tenant.Name == _tenantContext.TenantName, cancellationToken);

            if (agent == null)
            {
                return new NotFoundResult(nameof(TenantAgent), ErrorConst.AgentNotFound);
            }

            var chatHistory = new ChatHistory();
            foreach (var message in command.Messages)
            {
                var metadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.MessageId, !string.IsNullOrEmpty(message.MessageId) ? message.MessageId : Guid.NewGuid().ToString() }
                };
                if (message.Timestamp.HasValue)
                    metadata[CommonMetadataConsts.Timestamp] = message.Timestamp.Value;

                var role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant
                    : Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User;

                chatHistory.AddMessage(role, message.Text, metadata: metadata);
            }

            var (thumbsUp, thumbsDown) = await _chatStoreService.SaveChatAsync(
                _tenantContext.TenantName,
                command.Id,
                command.SessionId,
                command.Name,
                chatHistory,
                command.CreatedAt ?? _dateTimeProvider.DateTimeOffsetUtcNow(),
                command.UserId);

            await _mediator.Send(new SyncConversationCommand
            {
                AgentId = command.Id,
                TenantName = _tenantContext.TenantName,
                SessionId = command.SessionId,
                Name = command.Name,
                ConversationDate = command.CreatedAt ?? _dateTimeProvider.DateTimeOffsetUtcNow(),
                UserId = command.UserId,
                SubjectId = _chatContext.SubjectId,
                TotalThumbsUp = thumbsUp,
                TotalThumbsDown = thumbsDown,
                IsTestChat = _chatContext.IsTestChat
            }, cancellationToken);

            return new SuccessResult<bool>(true);
        }
    }
}
