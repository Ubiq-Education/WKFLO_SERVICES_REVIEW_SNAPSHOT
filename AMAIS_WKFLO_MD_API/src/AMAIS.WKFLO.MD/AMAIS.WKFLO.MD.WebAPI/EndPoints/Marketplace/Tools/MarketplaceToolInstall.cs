using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Tools
{
    /// <summary>
    /// Endpoint for installing marketplace tools into tenant
    /// </summary>
    public class MarketplaceToolInstall : BaseAsyncEndpoint<int, Result>
    {
        public MarketplaceToolInstall(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/marketplace/tools/{id}/install")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Tool")]
        [EndpointName("MarketplaceTool.Install")]
        [EndpointSummary("Install a Marketplace Tool")]
        [EndpointDescription("Install a Marketplace Tool into the current tenant as a new Tenant Tool")]
        [ProducesResponseType(typeof(SuccessResult<TenantToolCreateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            var command = new InstallMarketplaceToolCommand { Id = id };
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
