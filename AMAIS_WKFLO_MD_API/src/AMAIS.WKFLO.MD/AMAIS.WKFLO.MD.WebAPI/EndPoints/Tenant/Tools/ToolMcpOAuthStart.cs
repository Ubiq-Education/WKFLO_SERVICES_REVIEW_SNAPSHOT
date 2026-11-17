using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.StartToolMcpOAuth;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tools
{
    /// <summary>
    /// Starts the interactive OAuth2 consent flow for an MCP tool. Returns the authorization URL as JSON
    /// (rather than issuing an HTTP redirect) because this is an authenticated, tenant-scoped endpoint like
    /// any other — the caller (wkflo_ui) is expected to navigate the browser to <see cref="ToolMcpOAuthStartResult.AuthorizationUrl"/>
    /// itself. Only <see cref="ToolMcpOAuthCallback"/>, which the authorization server redirects the bare
    /// browser back to, is unauthenticated.
    /// </summary>
    public class ToolMcpOAuthStart : BaseAsyncEndpoint<Guid, Result>
    {
        public ToolMcpOAuthStart(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/tool/{id}/mcp/oauth/start")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tool")]
        [EndpointName("TenantTool.McpOAuthStart")]
        [EndpointSummary("Start the OAuth2 consent flow for an MCP tool")]
        [EndpointDescription("Begins connecting to an OAuth2-authenticated MCP server and returns the authorization URL to redirect the tenant's browser to.")]
        [ProducesResponseType(typeof(SuccessResult<ToolMcpOAuthStartResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] Guid id, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new StartToolMcpOAuthCommand { Id = id }, cancellationToken));
        }
    }
}
