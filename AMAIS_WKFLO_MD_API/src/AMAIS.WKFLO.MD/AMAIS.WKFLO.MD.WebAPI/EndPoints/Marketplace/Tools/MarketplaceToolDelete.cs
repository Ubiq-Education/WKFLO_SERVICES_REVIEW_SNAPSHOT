using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Tools
{
    public class MarketplaceToolDelete : BaseAsyncEndpoint<int, Result>
    {
        public MarketplaceToolDelete(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpDelete("/api/v1/marketplace/tool/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Tool")]
        [EndpointName("MarketplaceTool.Delete")]
        [EndpointSummary("Delete a Marketplace Tool")]
        [EndpointDescription("Delete a Marketplace Tool")]
        [ProducesResponseType(typeof(SuccessResult<MarketplaceToolDeleteResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DeleteMarketplaceToolCommand() { Id = id }, cancellationToken));
        }
    }
}
