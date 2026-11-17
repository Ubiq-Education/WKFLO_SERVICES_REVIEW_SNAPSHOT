using AMAIS.WKFLO.AI.Application.Tools.Commands.StartPublicToolMcpOAuth;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Tools
{
    /// <summary>
    /// The public chat widget's entry point to begin an MCP tool's OAuth 2.1 consent flow — the AI API
    /// counterpart of MD API's tenant-admin <c>tool/{id}/mcp/oauth/start</c> endpoint, for a tool attached to
    /// a publicly shared agent. Requires <see cref="AuthorizationMethod.AmaisJwtOnly"/> (not the default
    /// <see cref="AuthorizationMethod.AmaisJwtOrDatabase"/>): a share-id-only/database session carries no
    /// real WKFLO user identity, which a PerUser-scoped tool (e.g. Canva) requires — see
    /// <c>StartPublicToolMcpOAuthCommandHandler</c>.
    /// </summary>
    public class ToolMcpOAuthStart : BaseAsyncEndpoint<string, Guid, Result>
    {
        public ToolMcpOAuthStart(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization(AuthorizationMethod.AmaisJwtOnly)]
        [HttpGet("/api/v1/agent/{shareId}/tool/{id}/mcp/oauth/start")]
        [ProducesResponseType(typeof(SuccessResult<AMAIS.WKFLO.AI.Application.Tools.Models.ToolMcpOAuthStartResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Tool")]
        [EndpointName("PublicTool.StartMcpOAuth")]
        [EndpointSummary("Begins an MCP tool's OAuth2 consent flow from the public chat widget")]
        [EndpointDescription("Validates that the given MCP OAuth connection id belongs to the agent resolved by shareId, then begins the interactive OAuth 2.1 consent flow and returns the authorization URL to redirect the browser to.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, [FromRoute] Guid id, CancellationToken cancellationToken = default)
        {
            var command = new StartPublicToolMcpOAuthCommand { ShareId = shareId, Id = id };
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
