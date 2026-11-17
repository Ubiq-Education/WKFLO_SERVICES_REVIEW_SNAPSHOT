using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessageTenantAgentById
{
    public class VoiceMessageTenantAgentByIdCommandHandler : IRequestHandler<VoiceMessageTenantAgentByIdCommand, Result>
    {
        private readonly IWkfloMapper _mapper;
        private readonly ITenantAISyncService _tenantAISyncService;

        public VoiceMessageTenantAgentByIdCommandHandler(
            IWkfloMapper mapper,
            ITenantAISyncService tenantAISyncService)
        {
            _mapper = mapper;
            _tenantAISyncService = tenantAISyncService;
        }

        public async ValueTask<Result> Handle(VoiceMessageTenantAgentByIdCommand command, CancellationToken cancellationToken)
        {
            var tenantAgentData = await _tenantAISyncService.RunVoiceTenantAgentByIdAsync(command.AgentId, command.Text, command.SessionId, command.BranchId, command.ChainOfThought, cancellationToken);
            return tenantAgentData == null ? new NotFoundResult() : new SuccessResult<TenantAgentDataResult>(_mapper.Map<TenantAgentDataResult>(tenantAgentData));
        }
    }
}
