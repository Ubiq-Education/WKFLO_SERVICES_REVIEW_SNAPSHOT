using AMAIS.WKFLO.MD.Application.Tools.Settings;

namespace AMAIS.WKFLO.MD.Application.Tools.Services
{
    /// <summary>
    /// Builds the OAuth2 redirect_uri (callback URL) shared by every MCP OAuth connection. Extracted from
    /// <c>StartToolMcpOAuthCommandHandler</c> so the exact URL shape is defined exactly once. Deliberately a
    /// single static URL with no connection id (or any other id) in the path: the authorization server's
    /// `state` query parameter it echoes back on redirect already carries everything needed to correlate the
    /// callback to the right <c>McpOAuthConsentService.PendingFlow</c> (and, for a per-user tool, which user
    /// started it) — see <see cref="AMAIS.WKFLO.MD.Application.Tools.Commands.CompleteToolMcpOAuth.CompleteToolMcpOAuthCommandHandler"/>.
    /// This also means the same redirect_uri can be registered once with an OAuth provider instead of needing
    /// per-connection registration, which matters for providers like Canva that expect one fixed value.
    /// </summary>
    /// <remarks>
    /// Deliberately a single static URL, not one per connection — some authorization servers (e.g. Canva)
    /// require every redirect_uri to be pre-registered/allow-listed out of band (Canva's own MCP docs:
    /// "To add your redirect URI to the Canva MCP allowlist, apply for access with our Waitlist form"), which
    /// a URL that embeds a fresh per-tool <c>McpOAuthConnectionId</c> could never satisfy at any scale beyond
    /// one hand-registered connection. The callback instead resolves which in-flight connection a request
    /// belongs to via the OAuth <c>state</c> parameter alone (see <see cref="McpOAuthConsentService"/>) — the
    /// mechanism RFC 6749 §10.12 exists for: every spec-compliant authorization server echoes it back
    /// unchanged, which is exactly what a client sharing one redirect_uri across many concurrent flows needs.
    /// </remarks>
    public static class McpOAuthCallbackUrlBuilder
    {
        public static string Build(McpOAuthSettings settings)
        {
            var baseUrl = (settings?.PublicBaseUrl ?? string.Empty).TrimEnd('/');
            return $"{baseUrl}/md/api/v1/tenant/tool/mcp/oauth/callback";
        }
    }
}
