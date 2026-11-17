using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentsChatMessagesBySessionId;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentsChatMessagesBySessionIdList : BaseAsyncEndpoint<string, string, Result>
    {
        public PublicAgentsChatMessagesBySessionIdList(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization]
        [HttpGet("/api/v1/agent/{shareId}/chat/{sessionId}/session-messages")]
        [ProducesResponseType(typeof(SuccessResult<List<PublicAgentChatMessageResult>>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.ListChatMessagesBySessionId")]
        [EndpointSummary("List Public Agents Chat Messages by Session Id")]
        [EndpointDescription("Aggregates and lists chat messages from every agent that participated in the given chat session, ordered by timestamp. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, [FromRoute] string sessionId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListPublicAgentsChatMessagesBySessionIdQuery() { ShareId = shareId, SessionId = sessionId }, cancellationToken));
        }
    }
}
