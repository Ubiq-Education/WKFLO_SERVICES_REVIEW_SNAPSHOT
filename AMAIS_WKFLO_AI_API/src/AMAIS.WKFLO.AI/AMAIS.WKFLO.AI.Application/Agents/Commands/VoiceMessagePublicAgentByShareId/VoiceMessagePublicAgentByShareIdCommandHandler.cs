using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId
{
    public class VoiceMessagePublicAgentByShareIdCommandHandler : IRequestHandler<VoiceMessagePublicAgentByShareIdCommand, Result>
    {
        private readonly IWkfloMapper _mapper;
        private readonly IPublicAISyncService _publicAISyncService;

        public VoiceMessagePublicAgentByShareIdCommandHandler(
            IWkfloMapper mapper,
            IPublicAISyncService publicAISyncService)
        {
            _mapper = mapper;
            _publicAISyncService = publicAISyncService;
        }

        public async ValueTask<Result> Handle(VoiceMessagePublicAgentByShareIdCommand command, CancellationToken cancellationToken)
        {
            var tenantAgentData = await _publicAISyncService.RunVoicePublicAgentByIdAsync(command.ShareId, command.Text, command.SessionId, command.BranchId, command.ChainOfThought, cancellationToken);
            return tenantAgentData == null ? new NotFoundResult() : new SuccessResult<PublicAgentDataResult>(_mapper.Map<PublicAgentDataResult>(tenantAgentData));
        }
    }
}
