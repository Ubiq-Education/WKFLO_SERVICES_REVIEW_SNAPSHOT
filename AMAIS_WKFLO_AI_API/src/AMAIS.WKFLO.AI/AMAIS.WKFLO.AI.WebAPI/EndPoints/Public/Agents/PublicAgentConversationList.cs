using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentConversations;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentConversationList : BaseAsyncEndpoint<string, Result>
    {
        public PublicAgentConversationList(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization]
        [HttpGet("/api/v1/agent/{shareId}/chat/conversations")]
        [ProducesResponseType(typeof(SuccessResult<List<PublicAgentConversationResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.ListConversations")]
        [EndpointSummary("List Public Agent Conversations")]
        [EndpointDescription("Lists the caller's conversations with a Maisy-tagged agent: newest last activity first, up to 50 from the last 90 days, test chats excluded. Returns an empty list for anonymous callers and for agents not tagged Maisy. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListPublicAgentConversationsQuery() { ShareId = shareId }, cancellationToken));
        }
    }
}
