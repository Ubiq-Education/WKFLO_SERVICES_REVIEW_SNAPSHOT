using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.WipeLongTermMemoryTenantAgent
{
    public class WipeLongTermMemoryTenantAgentCommandHandler : IRequestHandler<WipeLongTermMemoryTenantAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IVectorStorageProvider _vectorStorageProvider;
        private readonly TenantContext _tenantContext;

        public WipeLongTermMemoryTenantAgentCommandHandler(IMasterDataDbContext db, IVectorStorageProvider vectorStorageProvider, TenantContext tenantContext)
        {
            _db = db;
            _vectorStorageProvider = vectorStorageProvider;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(WipeLongTermMemoryTenantAgentCommand command, CancellationToken cancellationToken)
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

            var affectedUsers = 0;

            foreach (var agentId in agentIds)
            {
                var userIds = await _db.Conversations
                    .AsNoTracking()
                    .Where(c => c.AgentId == agentId && c.TenantId == tenantId && !string.IsNullOrEmpty(c.UserId))
                    .Select(c => c.UserId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                affectedUsers += userIds?.Count ?? 0;

                foreach (var userId in userIds)
                {
                    var category = $"agent-{agentId}";
                    await _vectorStorageProvider.DeleteAsync(
                        userId,
                        record => record.Categories.Contains(category),
                        cancellationToken);
                }
            }

            return new SuccessResult<TenantAgentWipeLongTermMemoryResult>(new TenantAgentWipeLongTermMemoryResult
            {
                AgentId = command.Id,
                AffectedUsers = affectedUsers
            });
        }
    }
}
