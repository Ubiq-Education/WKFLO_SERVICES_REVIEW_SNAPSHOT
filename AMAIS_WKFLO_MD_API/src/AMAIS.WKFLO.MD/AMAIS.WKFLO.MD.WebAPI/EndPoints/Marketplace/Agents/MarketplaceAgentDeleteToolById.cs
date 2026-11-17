using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgentToolById;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Agents
{
    public class MarketplaceAgentDeleteToolById : BaseAsyncEndpoint<int, int, Result>
    {
        public MarketplaceAgentDeleteToolById(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpDelete("/api/v1/marketplace/agent/{id}/tool/{toolId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Agent")]
        [EndpointName("MarketplaceAgent.DeleteToolById")]
        [EndpointSummary("Delete a Marketplace Agent Tool by Id")]
        [EndpointDescription("Delete a Marketplace Agent Tool by Id")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public async override Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] int toolId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DeleteMarketplaceAgentToolByIdCommand() { Id = id, ToolId = toolId }, cancellationToken));
        }
    }
}
