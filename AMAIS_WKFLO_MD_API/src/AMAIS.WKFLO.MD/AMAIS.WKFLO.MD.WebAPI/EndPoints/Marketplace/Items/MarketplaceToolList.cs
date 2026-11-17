using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Items.Models;
using AMAIS.WKFLO.MD.Application.Items.Queries.ListMarketplaceItems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Items
{
    public class MarketplaceItemList : BaseAsyncEndpoint<Result>
    {
        public MarketplaceItemList(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/marketplace/items")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Item")]
        [EndpointName("MarketplaceItem.List")]
        [EndpointSummary("List the Marketplace Items")]
        [EndpointDescription("List the Marketplace Items")]
        [ProducesResponseType(typeof(SuccessResult<List<MarketplaceItemGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListMarketplaceItemsQuery(), cancellationToken));
        }
    }
}
