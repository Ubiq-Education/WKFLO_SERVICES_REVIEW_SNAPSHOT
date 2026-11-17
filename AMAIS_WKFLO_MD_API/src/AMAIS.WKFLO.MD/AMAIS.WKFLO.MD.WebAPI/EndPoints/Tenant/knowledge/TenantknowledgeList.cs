using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using AMAIS.WKFLO.MD.Application.Knowledge.Queries.ListTenantKnowledge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.knowledge
{
    public class TenantknowledgeList : BaseAsyncEndpoint<Result>
    {
        public TenantknowledgeList(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/tenant/knowledge")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Knowledge")]
        [EndpointName("TenantKnowledge.List")]
        [EndpointSummary("List the Tenant Knowledge")]
        [EndpointDescription("List the Tenant Knowledge")]
        [ProducesResponseType(typeof(SuccessResult<List<TenantKnowledgeGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListTenantKnowledgeQuery(), cancellationToken));
        }
    }
}
