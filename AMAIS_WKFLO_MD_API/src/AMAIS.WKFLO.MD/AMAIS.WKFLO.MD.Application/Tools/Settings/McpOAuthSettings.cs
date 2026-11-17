namespace AMAIS.WKFLO.MD.Application.Tools.Settings
{
    /// <summary>
    /// Settings for the interactive MCP OAuth2 consent flow (tenant/tool/start|status|disconnect, plus the
    /// shared tenant/tool/mcp/oauth/callback). Bound from the "McpOAuthSettings" configuration section /
    /// environment variables.
    /// </summary>
    public class McpOAuthSettings
    {
        /// <summary>
        /// Externally-reachable base URL of the BFF (e.g. https://wkflo.example.com — no trailing slash),
        /// used to build the OAuth redirect_uri (.../md/api/v1/tenant/tool/mcp/oauth/callback — one static
        /// URL shared by every connection in this environment, see McpOAuthCallbackUrlBuilder's remarks)
        /// backfilled onto every OAuth2 MCP tool's config.
        /// </summary>
        public string PublicBaseUrl { get; set; } = string.Empty;
    }
}
