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

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.WipeShortTermMemoryTenantAgent
{
    public class WipeShortTermMemoryTenantAgentCommandHandler : IRequestHandler<WipeShortTermMemoryTenantAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly IChatDocumentStore _chatDocumentStore;
        private readonly ILogger<WipeShortTermMemoryTenantAgentCommandHandler> _logger;

        public WipeShortTermMemoryTenantAgentCommandHandler(IMasterDataDbContext db, IChatStoreService chatStoreService, TenantContext tenantContext,
            IChatDocumentStore chatDocumentStore, ILogger<WipeShortTermMemoryTenantAgentCommandHandler> logger)
        {
            _db = db;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _chatDocumentStore = chatDocumentStore;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(WipeShortTermMemoryTenantAgentCommand command, CancellationToken cancellationToken)
        {
            var tenantId = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Name == _tenantContext.TenantName)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // Include the PublishedTenantAgents so conversations created through the
            // published version (different AgentId) are also wiped.
            var agentIds = new List<int> { command.Id };
            var publishedAgentIds = await _db.PublishedTenantAgents
                .AsNoTracking()
                .Where(a => a.CreatedByTenantAgentId == command.Id && a.TenantId == tenantId)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            agentIds.AddRange(publishedAgentIds);

            var conversations = await _db.Conversations
                .Where(c => agentIds.Contains(c.AgentId) && c.TenantId == tenantId && !c.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var conversation in conversations)
            {
                // Use conversation.AgentId (not command.Id) so the blob path matches
                // the agent the conversation was actually stored under.
                await _chatStoreService.DeleteChatAsync(_tenantContext.TenantName, conversation.AgentId, conversation.SessionId, conversation.UserId);

                // Bug 8189 / Task 8219: and this agent's long attached documents (a hand-off runs other agents in the same session).
                // Known limitation: documents have no scheduled purge, so a failure here leaves the chunks in chat_documents (unreachable,
                // still scoped to this tenant, session and user); the error log carries what is needed to remove them by hand.
                try
                {
                    await _chatDocumentStore.DeleteAgentDocumentsAsync(ChatDocumentScope.For(_tenantContext.TenantName, conversation.SessionId, conversation.UserId), [conversation.AgentId], cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Deleting the documents of agent {AgentId} in chat {SessionId} failed; their chunks stay in chat_documents (no scheduled purge)",
                        conversation.AgentId, conversation.SessionId);
                }

                conversation.IsDeleted = true;
            }

            if (conversations.Count > 0)
                await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantAgentWipeShortTermMemoryResult>(new TenantAgentWipeShortTermMemoryResult
            {
                AgentId = command.Id,
                DeletedConversations = conversations.Count
            });
        }
    }
}
