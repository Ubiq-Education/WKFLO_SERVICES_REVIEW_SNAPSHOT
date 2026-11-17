using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateAgentChatSettingShareable;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    [ApiController]
    public class TenantAgentChatSettingShareableUpdate : BaseAsyncEndpoint<int, UpdateAgentChatSettingShareableCommand, Result>
    {
        public TenantAgentChatSettingShareableUpdate(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPut("/api/v1/tenant/agent/{id}/chat-setting/sharable")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent", "Chat Setting", "Shareable")]
        [EndpointName("AgentChatSetting.UpdateShareable")]
        [EndpointSummary("Update Agent Chat Setting Shareable Status")]
        [EndpointDescription("Update the shareable status for an Agent Chat Setting")]
        [ProducesResponseType(typeof(SuccessResult<AgentChatSettingUpdateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateAgentChatSettingShareableCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
