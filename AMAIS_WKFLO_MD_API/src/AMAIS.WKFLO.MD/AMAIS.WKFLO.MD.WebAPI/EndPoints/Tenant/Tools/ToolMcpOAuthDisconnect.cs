using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DisconnectToolMcpOAuth;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tools
{
    public class ToolMcpOAuthDisconnect : BaseAsyncEndpoint<Guid, Result>
    {
        public ToolMcpOAuthDisconnect(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpDelete("/api/v1/tenant/tool/{id}/mcp/oauth")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tool")]
        [EndpointName("TenantTool.McpOAuthDisconnect")]
        [EndpointSummary("Disconnect an MCP tool's OAuth2 connection")]
        [EndpointDescription("Deletes the stored OAuth2 tokens for this MCP tool. The tenant must reconnect via the start endpoint to use the tool again.")]
        [ProducesResponseType(typeof(SuccessResult<ToolMcpOAuthDisconnectResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] Guid id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new DisconnectToolMcpOAuthCommand { Id = id }, cancellationToken));
        }
    }
}
