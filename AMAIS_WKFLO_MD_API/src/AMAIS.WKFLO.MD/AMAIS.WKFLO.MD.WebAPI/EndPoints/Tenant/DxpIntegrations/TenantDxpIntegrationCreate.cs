using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.CreateTenantDxpIntegration;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.DxpIntegrations
{
    public class TenantDxpIntegrationCreate : BaseAsyncEndpoint<CreateTenantDxpIntegrationCommand, Result>
    {
        public TenantDxpIntegrationCreate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/dxp-integration")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "DxpIntegration")]
        [EndpointName("TenantDxpIntegration.Create")]
        [EndpointSummary("Create the DXP Integration for the tenant")]
        [EndpointDescription("Creates the DXP Integration configuration for the current tenant. Only one integration per tenant is allowed.")]
        [ProducesResponseType(typeof(SuccessResult<DxpIntegrationCreateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CreateTenantDxpIntegrationCommand command, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
