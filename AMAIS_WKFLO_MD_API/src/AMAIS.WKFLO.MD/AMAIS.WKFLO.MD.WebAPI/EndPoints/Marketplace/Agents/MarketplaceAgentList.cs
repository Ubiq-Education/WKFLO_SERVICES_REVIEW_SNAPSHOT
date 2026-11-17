using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListMarketplaceAgents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Agents
{
    public class MarketplaceAgentList : BaseAsyncEndpoint<ListMarketplaceAgentsQuery, Result>
    {
        public MarketplaceAgentList(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/marketplace/agents")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Agent")]
        [EndpointName("MarketplaceAgent.List")]
        [EndpointSummary("List the Marketplace Agents")]
        [EndpointDescription("List the Marketplace Agents. Optionally filter to those tagged with a given Tag name via the `tag` query parameter (e.g. `?tag=AMAIS` for the curated template-library catalog).")]
        [ProducesResponseType(typeof(SuccessResult<List<MarketplaceAgentGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromQuery] ListMarketplaceAgentsQuery query, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
