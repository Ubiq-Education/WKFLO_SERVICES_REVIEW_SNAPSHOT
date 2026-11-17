using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentChatConfiguration;
using AMAIS.WKFLO.MD.WebAPI.EndPoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentChatSettingWidget : BaseAsyncEndpoint<int, Result>
    {
        public TenantAgentChatSettingWidget(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/agent/{agentId}/chat-setting/widget")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent", "Chat Setting")]
        [EndpointName("TenantAgent.GetChatSettingWidget")]
        [EndpointSummary("Get Agent Chat Setting for Widget")]
        [EndpointDescription("Get agent chat configuration specifically configured for widget usage within tenant context.")]
        [ProducesResponseType(typeof(SuccessResult<AgentChatSettingResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int agentId, CancellationToken cancellationToken = default)
        {
            var query = new GetTenantAgentChatSettingQuery { AgentId = agentId, IsWidget = true };
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
