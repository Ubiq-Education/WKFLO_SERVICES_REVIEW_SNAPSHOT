using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.UpdateTenantDxpIntegration;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.DxpIntegrations
{
    public class TenantDxpIntegrationUpdate : BaseAsyncEndpoint<int, UpdateTenantDxpIntegrationCommand, Result>
    {
        public TenantDxpIntegrationUpdate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPut("/api/v1/tenant/dxp-integration/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "DxpIntegration")]
        [EndpointName("TenantDxpIntegration.Update")]
        [EndpointSummary("Update the DXP Integration for the tenant")]
        [EndpointDescription("Updates the DXP Integration configuration for the current tenant.")]
        [ProducesResponseType(typeof(SuccessResult<DxpIntegrationUpdateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateTenantDxpIntegrationCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
