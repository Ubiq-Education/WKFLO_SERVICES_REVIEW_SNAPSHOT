using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgentChatMessages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentChatMessageList : BaseAsyncEndpoint<int, string, Result>
    {
        public TenantAgentChatMessageList(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/chat/{sessionId}/messages")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.ListChatMessages")]
        [EndpointSummary("List Tenant Agent Chat Messages")]
        [EndpointDescription("List Tenant Agent Chat Messages")]
        [ProducesResponseType(typeof(SuccessResult<List<TenantAgentChatMessageResult>>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] string sessionId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListTenantAgentChatMessagesQuery() { Id = id, SessionId = sessionId }, cancellationToken));
        }
    }
}
