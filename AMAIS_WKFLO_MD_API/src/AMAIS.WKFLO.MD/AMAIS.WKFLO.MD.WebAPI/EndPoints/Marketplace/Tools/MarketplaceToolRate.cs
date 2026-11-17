using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.RateMarketplaceTool;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Tools
{
    /// <summary>
    /// Endpoint for rating marketplace tools using UserStar entity
    /// </summary>
    public class MarketplaceToolRate : BaseAsyncEndpoint<int, RateMarketplaceToolCommand, Result>
    {
        public MarketplaceToolRate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/marketplace/tool/{id}/rate")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Tool")]
        [EndpointName("MarketplaceTool.Rate")]
        [EndpointSummary("Rate a Marketplace Tool")]
        [EndpointDescription("Rate a Marketplace Tool with a value between 1 and 5")]
        [ProducesResponseType(typeof(SuccessResult<Boolean>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromBody] RateMarketplaceToolCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
