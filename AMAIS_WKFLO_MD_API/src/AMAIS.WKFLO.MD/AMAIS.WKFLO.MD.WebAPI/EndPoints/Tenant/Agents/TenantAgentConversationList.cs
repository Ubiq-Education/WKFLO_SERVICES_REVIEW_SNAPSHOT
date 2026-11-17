using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgentConversations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentConversationList : BaseAsyncEndpoint<int, ListTenantAgentConversationsQuery, Result>
    {
        public TenantAgentConversationList(IWkfloMediator mediator) : base(mediator) { }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/conversations")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.ListConversations")]
        [EndpointSummary("List Tenant Agent Conversations")]
        [EndpointDescription("Returns a paginated list of conversations for the specified agent, ordered by most recent first. Defaults to page 1 with 15 items per page.")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentConversationsPageResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromQuery] ListTenantAgentConversationsQuery query, CancellationToken cancellationToken = default)
        {
            query.Id = id;
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
