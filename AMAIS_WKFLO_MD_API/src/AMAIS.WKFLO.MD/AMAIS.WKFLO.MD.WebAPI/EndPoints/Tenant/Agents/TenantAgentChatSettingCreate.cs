using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateAgentChatSetting;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentChatSettingCreate : BaseAsyncEndpoint<int, CreateAgentChatSettingCommand, Result>
    {
        public TenantAgentChatSettingCreate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{agentId}/chat-setting")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent", "Chat Setting")]
        [EndpointName("AgentChatSetting.Create")]
        [EndpointSummary("Create an Agent Chat Setting")]
        [EndpointDescription("Create an Agent Chat Setting for a tenant agent")]
        [ProducesResponseType(typeof(SuccessResult<AgentChatSettingCreateResult>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int agentId, CreateAgentChatSettingCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = agentId;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
