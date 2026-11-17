using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentChatStop : BaseAsyncEndpoint<int, StopTenantChatSessionCommand, Result>
    {
        public TenantAgentChatStop(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/chat/stop")]
        [ProducesResponseType(typeof(SuccessResult<StopChatSessionResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.StopChat")]
        [EndpointSummary("Stop every agent running in a tenant chat session")]
        [EndpointDescription("Hard cancel of the session's running turn(s), on whichever replica runs them. The chat gets typing off, the idle state and a ConversationStopped event straight away. Open tool approvals of the session expire and a deferred contact form replay is dropped.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, StopTenantChatSessionCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
