using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Models;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Queries.ListTenantAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.TenantAccesses
{
    public class TenantAccessList : BaseAsyncEndpoint<Result>
    {
        public TenantAccessList(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpGet("/api/v1/tenant-access")]
        [Tags("Main Area", "TenantAccess")]
        [EndpointName("TenantAccess.List")]
        [EndpointSummary("List the tenant access grants")]
        [EndpointDescription("Super power users only (403 otherwise).")]
        [ProducesResponseType(typeof(SuccessResult<List<TenantAccessResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ForbiddenResult), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListTenantAccessQuery(), cancellationToken));
        }
    }
}
