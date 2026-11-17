using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.AssignTagToEntity;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tags
{
    public class TenantTagAssign : BaseAsyncEndpoint<int, AssignTagToEntityCommand, Result>
    {
        public TenantTagAssign(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/tag/{tagId}/entity")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tag")]
        [EndpointName("TenantTag.Assign")]
        [EndpointSummary("Assign Tag to an entity")]
        [EndpointDescription("Assign a Tag to an entity (Agent, Tool, AIModel)")]
        [ProducesResponseType(typeof(SuccessResult<TagAssignResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int tagId, AssignTagToEntityCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = tagId;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
