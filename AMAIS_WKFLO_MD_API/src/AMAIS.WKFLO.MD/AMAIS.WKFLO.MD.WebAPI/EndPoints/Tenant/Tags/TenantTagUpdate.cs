using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.UpdateTag;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tags
{
    public class TenantTagUpdate : BaseAsyncEndpoint<int, UpdateTagCommand, Result>
    {
        public TenantTagUpdate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPut("/api/v1/tenant/tag/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tag")]
        [EndpointName("TenantTag.Update")]
        [EndpointSummary("Update a Tag")]
        [EndpointDescription("Update a Tag")]
        [ProducesResponseType(typeof(SuccessResult<TagUpdateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateTagCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
