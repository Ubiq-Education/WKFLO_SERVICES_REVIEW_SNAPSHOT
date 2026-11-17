using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetChatClassificationTopicByAgentId;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentGetChatClassificationTopicById : BaseAsyncEndpoint<int, Guid, Result>
    {
        public TenantAgentGetChatClassificationTopicById(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{id}/tools/chat-classification/topics/{topicId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent")]
        [EndpointName("TenantAgent.GetChatClassificationTopicById")]
        [EndpointSummary("Get the current version of a Chatbot Topic")]
        [EndpointDescription("Looks up a single Topic by id inside the agent's current Chat_Classification tool config and returns its latest data, without requiring the caller to fetch/parse the whole tool config.")]
        [ProducesResponseType(typeof(SuccessResult<ChatClassificationTopicGetResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, [FromRoute] Guid topicId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetChatClassificationTopicByAgentIdQuery() { AgentId = id, TopicId = topicId }, cancellationToken));
        }
    }
}
