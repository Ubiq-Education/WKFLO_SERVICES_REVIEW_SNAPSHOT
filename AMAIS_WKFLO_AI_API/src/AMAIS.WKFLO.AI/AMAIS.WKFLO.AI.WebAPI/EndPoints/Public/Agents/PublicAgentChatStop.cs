using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentChatStop : BaseAsyncEndpoint<string, StopPublicChatSessionCommand, Result>
    {
        public PublicAgentChatStop(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization(AuthorizationMethod.AmaisJwtOrDatabase)]
        [HttpPost("/api/v1/agent/{shareId}/chat/stop")]
        [ProducesResponseType(typeof(SuccessResult<StopChatSessionResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.StopChat")]
        [EndpointSummary("Stop every agent running in a shared chat session")]
        [EndpointDescription("Hard cancel of the session's running turn(s), on whichever replica runs them. The chat gets typing off, the idle state and a ConversationStopped event straight away. Open tool approvals of the session expire and a deferred contact form replay is dropped. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, StopPublicChatSessionCommand command, CancellationToken cancellationToken = default)
        {
            command.ShareId = shareId;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
