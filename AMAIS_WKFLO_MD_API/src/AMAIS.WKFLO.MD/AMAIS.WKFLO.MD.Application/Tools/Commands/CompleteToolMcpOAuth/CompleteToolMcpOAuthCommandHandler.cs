using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.CompleteToolMcpOAuth
{
    public class CompleteToolMcpOAuthCommandHandler : IRequestHandler<CompleteToolMcpOAuthCommand, Result>
    {
        private readonly IMcpOAuthConsentService _consentService;

        public CompleteToolMcpOAuthCommandHandler(IMcpOAuthConsentService consentService)
        {
            _consentService = consentService;
        }

        public async ValueTask<Result> Handle(CompleteToolMcpOAuthCommand command, CancellationToken cancellationToken)
        {
            // Deliberately not tenant-scoped, and (since the callback route is now a single static URL — see
            // CompleteToolMcpOAuthCommand's remarks) with no id of any kind to look a tool up by. There is no
            // separate "does a matching tool exist" check any more: command.State is the only thing that can
            // identify the right pending flow, and _consentService.CompleteAsync's own state-keyed lookup
            // already covers "unknown/forged/expired" in one place — a bogus request simply gets the same
            // InvalidResult as a genuinely expired or already-completed flow.
            if (string.IsNullOrWhiteSpace(command.Code))
                return new InvalidResult(nameof(command.Code), ErrorConst.ToolMcpOAuthConsentFailed);

            var connected = await _consentService.CompleteAsync(
                command.Code, command.State, command.Iss, cancellationToken);

            if (!connected)
                return new InvalidResult(nameof(CompleteToolMcpOAuthCommand.State), ErrorConst.ToolMcpOAuthNoPendingConsent);

            return new SuccessResult<ToolMcpOAuthCompleteResult>(new ToolMcpOAuthCompleteResult { Connected = true });
        }
    }
}
