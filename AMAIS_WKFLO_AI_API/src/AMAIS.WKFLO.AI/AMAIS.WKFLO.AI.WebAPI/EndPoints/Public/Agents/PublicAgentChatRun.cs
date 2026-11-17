using AMAIS.WKFLO.AI.Application.Agents.Commands.RunPublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentChatRun : BaseAsyncEndpoint<string, RunPublicAgentByShareIdCommand, Result>
    {
        public PublicAgentChatRun(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization]
        [HttpPost("/api/v1/agent/{shareId}/chat/run")]
        [ProducesResponseType(typeof(SuccessResult<PublicAgentDataResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.RunChat")]
        [EndpointSummary("Run a Public Agent by Id on a chat")]
        [EndpointDescription("Execute a public agent by ID. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, RunPublicAgentByShareIdCommand command, CancellationToken cancellationToken = default)
        {
            command.ShareId = shareId;
            command.Input?.Files.ClearServerOnlyFields();
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
