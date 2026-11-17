using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentChatMessages;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentChatMessageList : BaseAsyncEndpoint<string, string, Result>
    {
        public PublicAgentChatMessageList(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization]
        [HttpGet("/api/v1/agent/{shareId}/chat/{sessionId}/messages")]
        [ProducesResponseType(typeof(SuccessResult<List<PublicAgentChatMessageResult>>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.ListChatMessages")]
        [EndpointSummary("List Public Agent Chat Messages")]
        [EndpointDescription("List Public Agent Chat Messages. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, [FromRoute] string sessionId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListPublicAgentChatMessagesQuery() { ShareId = shareId, SessionId = sessionId }, cancellationToken));
        }
    }
}

