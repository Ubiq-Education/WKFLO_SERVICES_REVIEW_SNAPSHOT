using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantSettings.Models;
using AMAIS.WKFLO.MD.Application.TenantSettings.Queries.GetTenantSettings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.TenantSettings
{
    public class TenantSettingsGet : BaseAsyncEndpoint<Result>
    {
        public TenantSettingsGet(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/settings")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "TenantSettings")]
        [EndpointName("TenantSettings.Get")]
        [EndpointSummary("Get the tenant settings")]
        [EndpointDescription("Returns the current tenant's settings (time zone, time format). Settings never saved are returned with their defaults (UTC, 24h).")]
        [ProducesResponseType(typeof(SuccessResult<TenantSettingsResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetTenantSettingsQuery(), cancellationToken));
        }
    }
}
