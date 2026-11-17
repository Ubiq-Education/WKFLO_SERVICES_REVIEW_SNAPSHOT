using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatSessionState;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentChatSessionState : BaseAsyncEndpoint<string, string, Result>
    {
        public PublicAgentChatSessionState(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization]
        [HttpGet("/api/v1/agent/{shareId}/chat/{sessionId}/state")]
        [ProducesResponseType(typeof(SuccessResult<PublicAgentChatSessionStateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.GetChatSessionState")]
        [EndpointSummary("Get Public Agent Chat Session State")]
        [EndpointDescription("Whether any agent is still working on the chat session (a running turn or a queued hand-off), so a chat the user returns to can show it. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, [FromRoute] string sessionId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetPublicAgentChatSessionStateQuery() { ShareId = shareId, SessionId = sessionId }, cancellationToken));
        }
    }
}
