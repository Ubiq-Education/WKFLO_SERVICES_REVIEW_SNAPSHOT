using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentToolsById;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Agents
{
    public class MarketplaceAgentGetToolsById : BaseAsyncEndpoint<int, Result>
    {
        public MarketplaceAgentGetToolsById(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/marketplace/agent/{id}/tools")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Agent")]
        [EndpointName("MarketplaceAgent.GetToolsById")]
        [EndpointSummary("Get a Marketplace Agent Tools by Id")]
        [EndpointDescription("Get a Marketplace Agent Tools by Id")]
        [ProducesResponseType(typeof(SuccessResult<List<MarketplaceAgentToolGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetMarketplaceAgentToolsByIdQuery() { Id = id }, cancellationToken));
        }
    }
}
