using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Models;
using AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Queries.GetAgentTopicClassificationSummary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentTopicClassificationSummaryGet : BaseAsyncEndpoint<int, GetAgentTopicClassificationSummaryQuery, Result>
    {
        public TenantAgentTopicClassificationSummaryGet(IWkfloMediator mediator) : base(mediator) { }

        //TEMPORAL END POINT until we migrate to WKFLO Analytics, just to check model is doing the classification
        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/topic-classification-summary")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.GetTopicClassificationSummary")]
        [EndpointSummary("Get Agent Topic Classification Summary")]
        [EndpointDescription("Returns the count of classified messages grouped by topic name for the specified agent and date range.")]
        [ProducesResponseType(typeof(SuccessResult<List<AgentTopicClassificationSummaryResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(AMAIS.WKFLO.Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromQuery] GetAgentTopicClassificationSummaryQuery query, CancellationToken cancellationToken = default)
        {
            query.AgentId = id;
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
