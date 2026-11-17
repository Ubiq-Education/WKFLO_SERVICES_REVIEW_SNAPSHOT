using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetToolMcpOAuthStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tools
{
    public class ToolMcpOAuthStatus : BaseAsyncEndpoint<Guid, Result>
    {
        public ToolMcpOAuthStatus(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/tool/{id}/mcp/oauth/status")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tool")]
        [EndpointName("TenantTool.McpOAuthStatus")]
        [EndpointSummary("Get an MCP tool's OAuth2 connection status")]
        [EndpointDescription("Returns whether the tenant has completed the OAuth2 consent flow for this MCP tool, and since when.")]
        [ProducesResponseType(typeof(SuccessResult<ToolMcpOAuthStatusResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] Guid id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new GetToolMcpOAuthStatusQuery { Id = id }, cancellationToken));
        }
    }
}
