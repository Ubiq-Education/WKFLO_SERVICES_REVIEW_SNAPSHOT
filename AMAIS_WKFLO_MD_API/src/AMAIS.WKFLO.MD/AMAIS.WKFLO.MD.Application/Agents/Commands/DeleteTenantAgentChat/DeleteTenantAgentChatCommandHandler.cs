using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Documents;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgentChat
{
    public class DeleteTenantAgentChatCommandHandler : IRequestHandler<DeleteTenantAgentChatCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;
        private readonly IChatDocumentStore _chatDocumentStore;
        private readonly ILogger<DeleteTenantAgentChatCommandHandler> _logger;

        public DeleteTenantAgentChatCommandHandler(IMasterDataDbContext db, IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext,
            IChatDocumentStore chatDocumentStore, ILogger<DeleteTenantAgentChatCommandHandler> logger)
        {
            _db = db;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _chatDocumentStore = chatDocumentStore;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(DeleteTenantAgentChatCommand command, CancellationToken cancellationToken)
        {
            var isDeleted = await _chatStoreService.DeleteChatAsync(_tenantContext.TenantName, command.Id, command.SessionId, _userContext.Guid);

            if (isDeleted)
            {
                // Bug 8189 / Task 8219: this agent's long attached documents go with its chat (other agents of a hand-off keep theirs).
                // Known limitation: documents have no scheduled purge, so a failure here leaves the chunks in chat_documents (unreachable,
                // still scoped to this tenant, session and user); the error log carries what is needed to remove them by hand.
                try
                {
                    await _chatDocumentStore.DeleteAgentDocumentsAsync(ChatDocumentScope.For(_tenantContext.TenantName, command.SessionId, _userContext.Guid), [command.Id], cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Deleting the documents of agent {AgentId} in chat {SessionId} failed; their chunks stay in chat_documents (no scheduled purge)",
                        command.Id, command.SessionId);
                }

                var tenantId = await _db.Tenants
                    .AsNoTracking()
                    .Where(t => t.Name == _tenantContext.TenantName)
                    .Select(t => t.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                var conversation = await _db.Conversations
                    .Where(c => c.TenantId == tenantId && !c.IsDeleted && c.SessionId == command.SessionId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (conversation != null)
                {
                    conversation.IsDeleted = true;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            if (!isDeleted)
            {
                return new NotFoundResult(nameof(command.SessionId), $"Chat with session ID '{command.SessionId}' not found for agent ID '{command.Id}'.");
            }

            return new SuccessResult<TenantAgentChatDeleteResult>(new TenantAgentChatDeleteResult()
            {
                SessionId = command.SessionId,
            });
        }
    }
}
