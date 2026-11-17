using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.WebOrigins.Commands.SyncTenantWebOrigins;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.WebOrigins
{
    public class TenantWebOriginSync : BaseAsyncEndpoint<SyncTenantWebOriginsCommand, Result>
    {
        public TenantWebOriginSync(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPut("/api/v1/tenant/web-origins")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "WebOrigin")]
        [EndpointName("TenantWebOrigin.Sync")]
        [EndpointSummary("Sync Tenant Web Origins")]
        [EndpointDescription("Add-only merge of the provided web origins into the tenant: existing origins are never deleted or deactivated")]
        [ProducesResponseType(typeof(SuccessResult<List<TenantWebOriginResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(SyncTenantWebOriginsCommand command, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
