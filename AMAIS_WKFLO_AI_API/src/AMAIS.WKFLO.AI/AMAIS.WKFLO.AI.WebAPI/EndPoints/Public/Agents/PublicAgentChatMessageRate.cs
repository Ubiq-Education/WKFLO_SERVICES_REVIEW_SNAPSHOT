using AMAIS.WKFLO.AI.Application.Agents.Commands.RateMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentChatMessageRate : BaseAsyncEndpoint<string, RateMessagePublicAgentByShareIdCommand, Result>
    {
        public PublicAgentChatMessageRate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization]
        [HttpPost("/api/v1/agent/{shareId}/chat/message/rate")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.RateChatMessage")]
        [EndpointSummary("Rate an AI message in a public agent chat")]
        [EndpointDescription("Submit a thumbs up or thumbs down for a specific AI message. The message is identified by its timestamp. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, RateMessagePublicAgentByShareIdCommand command, CancellationToken cancellationToken = default)
        {
            command.ShareId = shareId;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}

