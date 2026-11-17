using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.SaveStoppedQueuedTurn
{
    public class SaveStoppedQueuedTurnCommandHandler : IRequestHandler<SaveStoppedQueuedTurnCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly ISemanticKernelAgentService _semanticKernelAgentService;

        public SaveStoppedQueuedTurnCommandHandler(IMasterDataDbContext db, TenantContext tenantContext, ISemanticKernelAgentService semanticKernelAgentService)
        {
            _db = db;
            _tenantContext = tenantContext;
            _semanticKernelAgentService = semanticKernelAgentService;
        }

        public async ValueTask<Result> Handle(SaveStoppedQueuedTurnCommand command, CancellationToken cancellationToken)
        {
            Agent agent = null;
            var rootIsWidget = command.RootIsWidget;

            if (!string.IsNullOrWhiteSpace(command.ShareId))
            {
                // Same lookup as the public chat run (PublicAISyncService), without what only the model call needs.
                var chatSetting = await _db.AgentChatSettings
                    .Include(acs => acs.Agent)
                    .Where(acs => acs.ShareId == command.ShareId && !acs.IsDeleted && !acs.Agent.IsDeleted && acs.Agent.Tenant.Name == _tenantContext.TenantName)
                    .FirstOrDefaultAsync(cancellationToken);

                agent = chatSetting?.Agent;
                rootIsWidget ??= chatSetting?.IsWidget;
            }
            else if (command.AgentId is int agentId)
            {
                agent = await _db.Agents
                    .Where(m => m.Id == agentId && !m.IsDeleted && m.Tenant.Name == _tenantContext.TenantName)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (agent is not RunnableAgent runnableAgent)
                return new NotFoundResult();

            await _semanticKernelAgentService.SaveTurnStoppedBeforeRunAsync(runnableAgent, command.SessionId, rootIsWidget);

            return new SuccessResult<bool>(true);
        }
    }
}
