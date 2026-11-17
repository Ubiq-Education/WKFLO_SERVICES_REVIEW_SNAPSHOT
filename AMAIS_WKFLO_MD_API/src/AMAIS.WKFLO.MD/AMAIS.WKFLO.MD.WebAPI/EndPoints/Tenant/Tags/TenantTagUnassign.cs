using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.UnassignTagFromEntity;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tags
{
    public class TenantTagUnassign : BaseAsyncEndpoint<Result>
    {
        public TenantTagUnassign(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpDelete("/api/v1/tenant/tag/{tagId}/entity/{entityType}/{entityId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tag")]
        [EndpointName("TenantTag.Unassign")]
        [EndpointSummary("Unassign Tag from an entity")]
        [EndpointDescription("Unassign a Tag from an entity (Agent, Tool, AIModel)")]
        [ProducesResponseType(typeof(SuccessResult<TagAssignResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            var tagId = int.Parse(RouteData.Values["tagId"]!.ToString()!);
            var entityType = RouteData.Values["entityType"]!.ToString()!;
            var entityId = int.Parse(RouteData.Values["entityId"]!.ToString()!);

            return AmaisWkfloResult(await Mediator.Send(new UnassignTagFromEntityCommand
            {
                Id = tagId,
                EntityType = entityType,
                EntityId = entityId
            }, cancellationToken));
        }
    }
}
