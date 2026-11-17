using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Queries.GetTenantDxpIntegration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.DxpIntegrations
{
    public class TenantDxpIntegrationGet : BaseAsyncEndpoint<Result>
    {
        public TenantDxpIntegrationGet(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/dxp-integration")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "DxpIntegration")]
        [EndpointName("TenantDxpIntegration.Get")]
        [EndpointSummary("Get the DXP Integration for the tenant")]
        [EndpointDescription("Returns the DXP Integration configuration for the current tenant.")]
        [ProducesResponseType(typeof(SuccessResult<DxpIntegrationGetResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetTenantDxpIntegrationQuery(), cancellationToken));
        }
    }
}
