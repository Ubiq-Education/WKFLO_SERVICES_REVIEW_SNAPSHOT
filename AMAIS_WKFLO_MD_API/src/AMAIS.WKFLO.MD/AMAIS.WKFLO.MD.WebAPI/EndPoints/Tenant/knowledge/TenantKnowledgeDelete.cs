using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.DeleteTenantKnowledge;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Knowledges
{
    public class TenantKnowledgeDelete : BaseAsyncEndpoint<string, Result>
    {
        public TenantKnowledgeDelete(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpDelete("/api/v1/tenant/knowledge/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Knowledge")]
        [EndpointName("TenantKnowledge.Delete")]
        [EndpointSummary("Delete a Tenant Knowledge")]
        [EndpointDescription("Delete a Tenant Knowledge")]
        [ProducesResponseType(typeof(SuccessResult<TenantKnowledgeDeleteResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DeleteTenantKnowledgeCommand() { Id = id }, cancellationToken));
        }
    }
}
