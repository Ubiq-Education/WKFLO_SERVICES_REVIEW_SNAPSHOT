using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgentToolById;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Agents
{
    public class MarketplaceAgentCreateToolById : BaseAsyncEndpoint<int, int, Result>
    {
        public MarketplaceAgentCreateToolById(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpPost("/api/v1/marketplace/agent/{id}/tool/{toolId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Agent")]
        [EndpointName("MarketplaceAgent.CreateToolById")]
        [EndpointSummary("Create a Marketplace Agent Tool by Id")]
        [EndpointDescription("Create a Marketplace Agent Tool by Id")]
        [ProducesResponseType(typeof(SuccessResult<MarketplaceAgentToolCreateResult>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] int toolId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new CreateMarketplaceAgentToolByIdCommand() { Id = id, ToolId = toolId }, cancellationToken));
        }
    }
}
