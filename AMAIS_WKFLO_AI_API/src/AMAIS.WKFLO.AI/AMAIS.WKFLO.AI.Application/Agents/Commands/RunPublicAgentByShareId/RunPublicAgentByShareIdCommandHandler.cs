using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.RunPublicAgentByShareId
{
    public class RunPublicAgentByShareIdCommandHandler : IRequestHandler<RunPublicAgentByShareIdCommand, Result>
    {
        private readonly IPublicAISyncService _publicAISyncService;
        private readonly IWkfloMapper _mapper;
        private readonly StoppableSyncRun _stoppableRun;

        public RunPublicAgentByShareIdCommandHandler(IPublicAISyncService publicAISyncService, IWkfloMapper mapper, StoppableSyncRun stoppableRun)
        {
            _publicAISyncService = publicAISyncService;
            _mapper = mapper;
            _stoppableRun = stoppableRun;
        }

        public async ValueTask<Result> Handle(RunPublicAgentByShareIdCommand command, CancellationToken cancellationToken)
        {
            // US 7501: only a real Stop cancels a chat turn. This synchronous run is bound to the HTTP request, so the
            // request's token (cancelled when the client disconnects) is not passed on: the run gets the turn's Stop
            // token instead, and otherwise finishes and is saved.
            var (publicAgentData, stopped) = await _stoppableRun.RunAsync(command.SessionId, new ChatSessionOwner { ShareId = command.ShareId }, stopToken =>
                _publicAISyncService.RunPublicAgentByShareIdAsync(command.ShareId, command.Input.Text, command.Input.Files, command.SessionId, cancellationToken: stopToken));

            if (stopped)
                return new SuccessResult<PublicAgentDataResult>(new PublicAgentDataResult { Stopped = true });

            return publicAgentData == null ? new NotFoundResult() : new SuccessResult<PublicAgentDataResult>(_mapper.Map<PublicAgentDataResult>(publicAgentData));
        }
    }
}
