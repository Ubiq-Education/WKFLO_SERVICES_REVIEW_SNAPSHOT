using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.RemoveTenantAccess;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.TenantAccesses
{
    public class TenantAccessRemove : BaseAsyncEndpoint<int, Result>
    {
        public TenantAccessRemove(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpDelete("/api/v1/tenant-access/{id}")]
        [Tags("Main Area", "TenantAccess")]
        [EndpointName("TenantAccess.Remove")]
        [EndpointSummary("Remove a tenant access grant")]
        [EndpointDescription("Super power users only (403 otherwise). Soft-deletes.")]
        [ProducesResponseType(typeof(SuccessResult<TenantAccessResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ForbiddenResult), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new RemoveTenantAccessCommand { Id = id }, cancellationToken));
        }
    }
}
