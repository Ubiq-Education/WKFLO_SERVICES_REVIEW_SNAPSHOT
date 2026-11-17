using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Permissions.Models;
using AMAIS.WKFLO.MD.Application.Permissions.Queries.GetMyPermissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Permissions
{
    public class MyPermissionsGet : BaseAsyncEndpoint<Result>
    {
        public MyPermissionsGet(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpGet("/api/v1/me/permissions")]
        [Tags("Main Area", "Permissions")]
        [EndpointName("Permissions.GetMine")]
        [EndpointSummary("Get the permissions of the calling user")]
        [EndpointDescription("Returns whether the caller is a super power user and, if not, the guids of the tenants they may access. Global: does not require the tenant header.")]
        [ProducesResponseType(typeof(SuccessResult<MyPermissionsResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetMyPermissionsQuery(), cancellationToken));
        }
    }
}
