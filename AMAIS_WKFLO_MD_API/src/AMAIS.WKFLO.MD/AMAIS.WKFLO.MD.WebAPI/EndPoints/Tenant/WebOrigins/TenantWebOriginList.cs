using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using AMAIS.WKFLO.MD.Application.WebOrigins.Queries.ListTenantWebOrigins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.WebOrigins
{
    public class TenantWebOriginList : BaseAsyncEndpoint<Result>
    {
        public TenantWebOriginList(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/web-origins")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "WebOrigin")]
        [EndpointName("TenantWebOrigin.List")]
        [EndpointSummary("List Tenant Web Origins")]
        [EndpointDescription("Lists every stored web origin for the tenant, including inactive and soft-deleted rows. Super power users only (403 otherwise).")]
        [ProducesResponseType(typeof(SuccessResult<List<TenantWebOriginListResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ForbiddenResult), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListTenantWebOriginsQuery(), cancellationToken));
        }
    }
}
