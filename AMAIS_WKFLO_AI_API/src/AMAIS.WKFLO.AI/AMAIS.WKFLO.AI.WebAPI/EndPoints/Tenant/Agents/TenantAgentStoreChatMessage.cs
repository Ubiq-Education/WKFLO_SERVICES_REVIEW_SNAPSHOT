using AMAIS.WKFLO.AI.Application.Agents.Commands.StoreMessageTenantAgentById;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentStoreChatMessage : BaseAsyncEndpoint<int, StoreMessageTenantAgentByIdCommand, Result>
    {
        public TenantAgentStoreChatMessage(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/store/chat/message")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.StoreMessageChat")]
        [EndpointSummary("Store Message for a Tenant Agent by Id on a chat")]
        [EndpointDescription("Store a tenant agent message by agent Id.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, StoreMessageTenantAgentByIdCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
