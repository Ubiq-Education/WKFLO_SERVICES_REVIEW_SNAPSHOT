using AMAIS.WKFLO.AI.Application.Agents.Commands.RateMessageTenantAgentById;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentChatMessageRate : BaseAsyncEndpoint<int, RateMessageTenantAgentByIdCommand, Result>
    {
        public TenantAgentChatMessageRate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/chat/message/rate")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.RateChatMessage")]
        [EndpointSummary("Rate an AI message in a tenant agent chat")]
        [EndpointDescription("Submit a thumbs up or thumbs down for a specific AI message. The message is identified by its timestamp.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, RateMessageTenantAgentByIdCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}

