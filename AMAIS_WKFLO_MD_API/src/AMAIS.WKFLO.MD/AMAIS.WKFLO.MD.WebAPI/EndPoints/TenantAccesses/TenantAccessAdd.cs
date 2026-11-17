using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.AddTenantAccess;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.TenantAccesses
{
    public class TenantAccessAdd : BaseAsyncEndpoint<AddTenantAccessCommand, Result>
    {
        public TenantAccessAdd(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpPost("/api/v1/tenant-access")]
        [Tags("Main Area", "TenantAccess")]
        [EndpointName("TenantAccess.Add")]
        [EndpointSummary("Grant an email access to a tenant")]
        [EndpointDescription("Super power users only (403 otherwise). Re-adding a removed grant reactivates it.")]
        [ProducesResponseType(typeof(SuccessResult<TenantAccessResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ForbiddenResult), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromBody] AddTenantAccessCommand command, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
