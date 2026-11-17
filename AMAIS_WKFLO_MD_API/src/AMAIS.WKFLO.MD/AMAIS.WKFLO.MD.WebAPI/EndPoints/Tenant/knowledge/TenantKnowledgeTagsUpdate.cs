using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.UpdateTenantKnowledgeTags;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.knowledge
{
    public class TenantKnowledgeTagsUpdate : BaseAsyncEndpoint<string, UpdateTenantKnowledgeTagsCommand, Result>
    {

        public TenantKnowledgeTagsUpdate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPut("/api/v1/tenant/knowledge/{id}/tags")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Knowledge")]
        [EndpointName("TenantKnowledge.UpdateTags")]
        [EndpointSummary("Update tags for a Tenant Knowledge document")]
        [EndpointDescription("Updates the tags for a specific knowledge document")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string id, [FromBody] UpdateTenantKnowledgeTagsCommand command, CancellationToken cancellationToken = default)
        {
            command.DocId = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
