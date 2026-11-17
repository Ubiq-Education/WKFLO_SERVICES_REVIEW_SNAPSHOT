using AMAIS.WKFLO.AI.Application.Agents.Commands.StoreMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentStoreChatMessage : BaseAsyncEndpoint<string, StoreMessagePublicAgentByShareIdCommand, Result>
    {
        public PublicAgentStoreChatMessage(IWkfloMediator mediator, DaprClient daprClient, TenantContext tenantContext, ChatContext chatContext
            , UserContext userContext, AgentSharableContext agentSharableContext) : base(mediator)
        {
        }

        [WebChatAuthorization]
        [HttpPost("/api/v1/agent/{shareId}/store/chat/message")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.StoreMessageChat")]
        [EndpointSummary("Store Message for a Public Agent by Id on a chat")]
        [EndpointDescription("Sotre a public agent messaage by agent Id. Authorization requirements are determined by database configuration.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, StoreMessagePublicAgentByShareIdCommand command, CancellationToken cancellationToken = default)
        {
            command.ShareId = shareId;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
