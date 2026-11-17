using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.RemoveSuperPowerUser;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.SuperPowerUsers
{
    public class SuperPowerUserRemove : BaseAsyncEndpoint<int, Result>
    {
        public SuperPowerUserRemove(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpDelete("/api/v1/super-power-users/{id}")]
        [Tags("Main Area", "SuperPowerUser")]
        [EndpointName("SuperPowerUser.Remove")]
        [EndpointSummary("Remove a super power user")]
        [EndpointDescription("Super power users only (403 otherwise). Soft-deletes. Removing yourself or the last super power user is rejected.")]
        [ProducesResponseType(typeof(SuccessResult<SuperPowerUserResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ForbiddenResult), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new RemoveSuperPowerUserCommand { Id = id }, cancellationToken));
        }
    }
}
