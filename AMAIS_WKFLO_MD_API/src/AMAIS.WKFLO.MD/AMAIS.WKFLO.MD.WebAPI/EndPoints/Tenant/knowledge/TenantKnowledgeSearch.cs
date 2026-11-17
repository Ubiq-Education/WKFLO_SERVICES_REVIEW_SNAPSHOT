using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using AMAIS.WKFLO.MD.Application.Knowledge.Queries.SearchTenantKnowledge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Knowledge
{
    public class TenantKnowledgeSearch : BaseAsyncEndpoint<SearchTenantKnowledgeQuery, Result>
    {
        public TenantKnowledgeSearch(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpPost("/api/v1/tenant/knowledge/query")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Knowledge")]
        [EndpointName("TenantKnowledge.Search")]
        [EndpointSummary("Search Tenant Knowledge")]
        [EndpointDescription("Search Tenant Knowledge")]
        [ProducesResponseType(typeof(SuccessResult<List<TenantKnowledgeSearchResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(SearchTenantKnowledgeQuery query, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
