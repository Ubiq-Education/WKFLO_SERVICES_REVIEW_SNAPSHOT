using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListTenantAgentChatMessages;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentChatMessageList : BaseAsyncEndpoint<int, string, Result>
    {
        public TenantAgentChatMessageList(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/chat/{sessionId}/messages")]
        [ProducesResponseType(typeof(SuccessResult<List<TenantAgentChatMessageResult>>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.ListChatMessages")]
        [EndpointSummary("List Tenant Agent Chat Messages")]
        [EndpointDescription("List Tenant Agent Chat Messages. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] string sessionId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListTenantAgentChatMessagesQuery() { Id = id, SessionId = sessionId }, cancellationToken));
        }
    }
}

