using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tenants.Commands.CreateTenant;
using AMAIS.WKFLO.MD.Application.Tenants.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant
{
    public class TenantCreate : BaseAsyncEndpoint<CreateTenantCommand, Result>
    {
        public TenantCreate(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpPost("/api/v1/tenant")]
        [Tags("Main Area", "Tenant")]
        [EndpointName("Tenant.Create")]
        [EndpointSummary("Create a Tenant")]
        [EndpointDescription("Create a Tenant. Requires either 'access_as_user' scope (for user authentication) or 'Api.Access' app role (for machine-to-machine authentication).")]
        [ProducesResponseType(typeof(SuccessResult<TenantCreateResult>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
