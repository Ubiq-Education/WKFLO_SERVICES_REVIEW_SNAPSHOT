using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.GetAvailableSpinUpAgentsByShareId;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentSpinUpAgents : BaseAsyncEndpoint<string, Result>
    {
        public PublicAgentSpinUpAgents(IWkfloMediator mediator) : base(mediator)
        {
        }

        [AllowAnonymous]
        [HttpGet("/api/v1/agent/{shareId}/spin-up/agents")]
        [ProducesResponseType(typeof(SuccessResult<List<AgentSpinUpTargetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.GetAvailableSpinUpAgents")]
        [EndpointSummary("Get Available Spin-Up Agents")]
        [EndpointDescription("Lists the name and chat icon URL of every other active runnable agent in the same tenant as the shared agent — the data needed to build an @name target picker for publish_agent_event. This endpoint is public and does not require authentication.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(shareId))
            {
                return BadRequest(new InvalidResult(nameof(shareId), "ShareId is required"));
            }

            var query = new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = shareId };
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
