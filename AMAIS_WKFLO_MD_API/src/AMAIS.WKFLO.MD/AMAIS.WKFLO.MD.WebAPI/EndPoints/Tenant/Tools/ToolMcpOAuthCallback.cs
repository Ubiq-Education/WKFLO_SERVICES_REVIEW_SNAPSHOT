using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CompleteToolMcpOAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tools
{
    /// <summary>
    /// Where MCP servers (e.g. Humantic, Canva) redirect the tenant's browser back to after consent. Unlike
    /// every other tenant endpoint this is a bare top-level browser navigation the authorization server
    /// controls — it cannot carry our Bearer token or tenant header — so it is intentionally unauthenticated
    /// and not tenant-scoped (see <see cref="CompleteToolMcpOAuthCommand"/>'s remarks). It's also a single
    /// static route with no connection id (or any other id) in the path: the `state` query parameter the
    /// authorization server echoes back is what correlates this request to the right pending consent flow —
    /// see <see cref="Services.McpOAuthCallbackUrlBuilder"/> and <c>McpOAuthConsentService.CompleteAsync</c>.
    /// It always renders a small human-readable HTML page rather than a JSON body, since a person's browser
    /// lands here directly; wkflo_ui learns the real outcome by polling <see cref="ToolMcpOAuthStatus"/>.
    /// </summary>
    public class ToolMcpOAuthCallback : BaseAsyncEndpoint<ToolMcpOAuthCallbackQuery, Result>
    {
        public ToolMcpOAuthCallback(IWkfloMediator mediator) : base(mediator)
        {
        }

        [AllowAnonymous]
        [HttpGet("/api/v1/tenant/tool/mcp/oauth/callback")]
        [Tags("Tool")]
        [EndpointName("TenantTool.McpOAuthCallback")]
        [EndpointSummary("OAuth2 redirect target for an MCP tool's consent flow")]
        [EndpointDescription("Receives the authorization code from the MCP server's OAuth redirect and completes the token exchange.")]
        [ProducesResponseType(typeof(ContentResult), StatusCodes.Status200OK)]
        public override async Task<ActionResult<Result>> HandleAsync([FromQuery] ToolMcpOAuthCallbackQuery query, CancellationToken cancellationToken = default)
        {
            var result = await Mediator.Send(new CompleteToolMcpOAuthCommand
            {
                Code = query?.Code,
                State = query?.State,
                Iss = query?.Iss
            }, cancellationToken);

            var connected = result.ResultType == ResultType.Ok;
            return Content(BuildHtml(connected), "text/html");
        }

        private static string BuildHtml(bool connected)
        {
            var title = connected ? "Connected" : "Connection failed";
            var message = connected
                ? "You're connected. You can close this window."
                : "We couldn't complete the connection. Please close this window and try again.";

            return $"""
                <!doctype html>
                <html>
                <head><meta charset="utf-8"><title>{title}</title></head>
                <body style="font-family: sans-serif; text-align: center; padding-top: 3rem;">
                <p>{message}</p>
                </body>
                </html>
                """;
        }
    }

    /// <summary>Query-string parameters an OAuth authorization server sends on its redirect back to us.</summary>
    public class ToolMcpOAuthCallbackQuery
    {
        public string Code { get; set; }
        public string State { get; set; }
        public string Iss { get; set; }
    }
}
