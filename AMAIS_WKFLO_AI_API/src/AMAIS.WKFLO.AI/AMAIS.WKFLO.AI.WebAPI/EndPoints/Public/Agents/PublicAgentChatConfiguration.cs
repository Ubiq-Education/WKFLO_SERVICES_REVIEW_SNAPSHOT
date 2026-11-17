using AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatConfiguration;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentChatConfiguration : BaseAsyncEndpoint<string, Result>
    {
        public PublicAgentChatConfiguration(IWkfloMediator mediator) : base(mediator)
        {
        }

        [AllowAnonymous]
        [HttpGet("/api/v1/agent/{shareId}/chat/configuration")]
        [ProducesResponseType(typeof(SuccessResult<AgentChatSettingResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.GetChatConfiguration")]
        [EndpointSummary("Get Agent Chat Configuration")]
        [EndpointDescription("Get configuration data required by the agent chat widget. This endpoint is public and does not require authentication.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(shareId))
            {
                return BadRequest(new InvalidResult(nameof(shareId), "ShareId is required"));
            }

            var query = new GetPublicAgentChatSettingQuery { ShareId = shareId };
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
