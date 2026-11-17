namespace AMAIS.WKFLO.AI.Application.Tools.Models
{
    /// <summary>Same response shape as MD API's tenant <c>tool/{id}/mcp/oauth/start</c> endpoint
    /// (<c>ToolMcpOAuthStartResult</c> there) — kept as its own type here since AI API doesn't reference MD
    /// API's Application layer.</summary>
    public class ToolMcpOAuthStartResult
    {
        /// <summary>The MCP server's OAuth authorize URL (with PKCE challenge) the browser must be sent to next.</summary>
        public string AuthorizationUrl { get; set; }
    }
}
