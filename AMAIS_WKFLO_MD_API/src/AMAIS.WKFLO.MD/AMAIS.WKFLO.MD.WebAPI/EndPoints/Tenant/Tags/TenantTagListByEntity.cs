using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using AMAIS.WKFLO.MD.Application.Tags.Queries.ListTagsByEntity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tags
{
    public class TenantTagListByEntity : BaseAsyncEndpoint<Result>
    {
        public TenantTagListByEntity(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/entity/{entityType}/{entityId}/tags")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tag")]
        [EndpointName("TenantTag.ListByEntity")]
        [EndpointSummary("List Tags for an entity")]
        [EndpointDescription("List all Tags assigned to an entity (Agent, Tool, AIModel)")]
        [ProducesResponseType(typeof(SuccessResult<List<TagGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            var entityType = RouteData.Values["entityType"]!.ToString()!;
            var entityId = int.Parse(RouteData.Values["entityId"]!.ToString()!);

            return AmaisWkfloResult(await Mediator.Send(new ListTagsByEntityQuery
            {
                EntityType = entityType,
                EntityId = entityId
            }, cancellationToken));
        }
    }
}
