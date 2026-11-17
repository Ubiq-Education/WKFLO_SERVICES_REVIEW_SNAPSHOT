using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Models;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Queries.ListSuperPowerUsers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.SuperPowerUsers
{
    public class SuperPowerUserList : BaseAsyncEndpoint<Result>
    {
        public SuperPowerUserList(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpGet("/api/v1/super-power-users")]
        [Tags("Main Area", "SuperPowerUser")]
        [EndpointName("SuperPowerUser.List")]
        [EndpointSummary("List the super power users")]
        [EndpointDescription("Super power users only (403 otherwise).")]
        [ProducesResponseType(typeof(SuccessResult<List<SuperPowerUserResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ForbiddenResult), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListSuperPowerUsersQuery(), cancellationToken));
        }
    }
}
