using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.DeleteTag;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tags
{
    public class TenantTagDelete : BaseAsyncEndpoint<int, Result>
    {
        public TenantTagDelete(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpDelete("/api/v1/tenant/tag/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tag")]
        [EndpointName("TenantTag.Delete")]
        [EndpointSummary("Delete a Tag")]
        [EndpointDescription("Soft-delete a Tag")]
        [ProducesResponseType(typeof(SuccessResult<TagDeleteResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DeleteTagCommand { Id = id }, cancellationToken));
        }
    }
}
