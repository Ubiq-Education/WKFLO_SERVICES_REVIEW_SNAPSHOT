using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentConversationBySessionId;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentConversationGet : BaseAsyncEndpoint<int, string, Result>
    {
        public TenantAgentConversationGet(IWkfloMediator mediator) : base(mediator) { }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/conversations/{sessionId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.GetConversationBySessionId")]
        [EndpointSummary("Get Tenant Agent Conversation by Session ID")]
        [EndpointDescription("Returns a single conversation for the specified agent identified by session ID.")]
        [ProducesResponseType(typeof(SuccessResult<TenantAgentConversationResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(AMAIS.WKFLO.Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] string sessionId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetTenantAgentConversationBySessionIdQuery
            {
                Id = id,
                SessionId = sessionId
            }, cancellationToken));
        }
    }
}
