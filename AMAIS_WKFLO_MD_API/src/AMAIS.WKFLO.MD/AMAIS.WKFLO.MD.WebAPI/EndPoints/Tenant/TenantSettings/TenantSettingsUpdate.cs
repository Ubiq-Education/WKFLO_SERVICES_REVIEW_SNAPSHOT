using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantSettings.Commands.UpdateTenantSettings;
using AMAIS.WKFLO.MD.Application.TenantSettings.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.TenantSettings
{
    public class TenantSettingsUpdate : BaseAsyncEndpoint<UpdateTenantSettingsCommand, Result>
    {
        public TenantSettingsUpdate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPut("/api/v1/tenant/settings")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "TenantSettings")]
        [EndpointName("TenantSettings.Update")]
        [EndpointSummary("Update the tenant settings")]
        [EndpointDescription("Updates the current tenant's settings. Omitted or null properties are left unchanged. TimeZone accepts an IANA id (or a Windows id, stored as IANA); TimeFormat accepts \"12h\" or \"24h\"; DateFormat accepts one of the whitelisted .NET date patterns (e.g. \"dd/MM/yyyy\", \"MM/dd/yyyy\", \"yyyy-MM-dd\", \"MMM dd, yyyy\"; case-sensitive). Returns the full settings after the update.")]
        [ProducesResponseType(typeof(SuccessResult<TenantSettingsResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(UpdateTenantSettingsCommand command, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
