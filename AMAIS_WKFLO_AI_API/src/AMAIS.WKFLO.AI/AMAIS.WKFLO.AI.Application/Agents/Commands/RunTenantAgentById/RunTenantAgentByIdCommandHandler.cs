using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.RunTenantAgentById
{
    public class RunTenantAgentByIdCommandHandler : IRequestHandler<RunTenantAgentByIdCommand, Result>
    {
        private readonly ITenantAISyncService _tenantAISyncService;
        private readonly IWkfloMapper _mapper;
        private readonly StoppableSyncRun _stoppableRun;

        public RunTenantAgentByIdCommandHandler(ITenantAISyncService tenantAISyncService, IWkfloMapper mapper, StoppableSyncRun stoppableRun)
        {
            _tenantAISyncService = tenantAISyncService;
            _mapper = mapper;
            _stoppableRun = stoppableRun;
        }

        public async ValueTask<Result> Handle(RunTenantAgentByIdCommand command, CancellationToken cancellationToken)
        {
            // US 7501: only a real Stop cancels a chat turn. This synchronous run is bound to the HTTP request, so the
            // request's token (cancelled when the client disconnects) is not passed on: the run gets the turn's Stop
            // token instead, and otherwise finishes and is saved.
            var (tenantAgentData, stopped) = await _stoppableRun.RunAsync(command.SessionId, new ChatSessionOwner { AgentId = command.Id }, stopToken =>
                _tenantAISyncService.RunTenantAgentByIdAsync(command.Id, command.Input.Text, command.Input.Files, command.SessionId, true, cancellationToken: stopToken));

            if (stopped)
                return new SuccessResult<TenantAgentDataResult>(new TenantAgentDataResult { Stopped = true });

            return tenantAgentData == null ? new NotFoundResult() : new SuccessResult<TenantAgentDataResult>(_mapper.Map<TenantAgentDataResult>(tenantAgentData));
        }
    }
}
