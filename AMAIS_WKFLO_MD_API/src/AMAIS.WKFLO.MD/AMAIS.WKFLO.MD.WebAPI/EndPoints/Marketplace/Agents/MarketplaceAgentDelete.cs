using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Agents
{
    public class MarketplaceAgentDelete : BaseAsyncEndpoint<int, Result>
    {
        public MarketplaceAgentDelete(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpDelete("/api/v1/marketplace/agent/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Agent")]
        [EndpointName("MarketplaceAgent.Delete")]
        [EndpointSummary("Delete a Marketplace Agent")]
        [EndpointDescription("Delete a Marketplace Agent")]
        [ProducesResponseType(typeof(SuccessResult<MarketplaceAgentDeleteResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DeleteMarketplaceAgentCommand() { Id = id }, cancellationToken));
        }
    }
}
