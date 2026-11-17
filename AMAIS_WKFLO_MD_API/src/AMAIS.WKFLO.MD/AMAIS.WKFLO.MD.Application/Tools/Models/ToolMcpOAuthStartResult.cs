namespace AMAIS.WKFLO.MD.Application.Tools.Models
{
    public class ToolMcpOAuthStartResult
    {
        /// <summary>The MCP server's OAuth authorize URL (with PKCE challenge) the browser must be sent to next.</summary>
        public string AuthorizationUrl { get; set; }
    }
}
