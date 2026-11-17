using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.AddSuperPowerUser;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.SuperPowerUsers
{
    public class SuperPowerUserAdd : BaseAsyncEndpoint<AddSuperPowerUserCommand, Result>
    {
        public SuperPowerUserAdd(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [HttpPost("/api/v1/super-power-users")]
        [Tags("Main Area", "SuperPowerUser")]
        [EndpointName("SuperPowerUser.Add")]
        [EndpointSummary("Add a super power user")]
        [EndpointDescription("Super power users only (403 otherwise). Re-adding a removed email reactivates it.")]
        [ProducesResponseType(typeof(SuccessResult<SuperPowerUserResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ForbiddenResult), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromBody] AddSuperPowerUserCommand command, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
