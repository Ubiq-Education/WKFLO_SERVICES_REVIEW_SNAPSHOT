using AMAIS.WKFLO.Common.Models.Config;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// Drives the interactive OAuth 2.1 + PKCE (RFC 7636) consent flow for an OAuth2-authenticated MCP tool,
    /// across the two independent HTTP requests it necessarily spans — "start"
    /// (<see cref="BeginAsync"/>, e.g. the MD API's tenant admin dashboard or the AI API's public chat
    /// widget) and "callback" (<see cref="CompleteAsync"/>, reached by the authorization server's browser
    /// redirect, always on the MD API's single static callback route — see
    /// <c>McpOAuthCallbackUrlBuilder</c>). Both methods are multi-replica-safe: nothing survives between
    /// calls except a <see cref="Entities.McpOAuthPendingFlow"/> row in SQL, so "start" and "callback" may
    /// land on two entirely different process instances (see Bug 7129).
    /// </summary>
    public interface IMcpOAuthConsentService
    {
        /// <summary>
        /// Discovers the MCP server's authorization server, dynamically registers a client if
        /// <paramref name="serverConfig"/> has none configured, generates a PKCE verifier/challenge and OAuth
        /// `state`, persists the resulting pending flow, and returns the authorization URL the caller should
        /// send the browser to next. Returns as soon as the URL is built — no waiting on the tenant's
        /// consent.
        /// </summary>
        Task<Uri> BeginAsync(McpServerConfig serverConfig, Guid connectionId, Guid? requestingUserId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Looks up the pending flow by <paramref name="state"/>, validates <paramref name="iss"/> if
        /// present, exchanges <paramref name="code"/> for tokens, persists them, verifies the MCP handshake
        /// actually works with the new token, and records the outcome. Returns <see langword="false"/> for
        /// any unrecognized/expired/already-completed state — the same external contract as before this
        /// service's persisted-state rewrite (Task 7130).
        /// </summary>
        Task<bool> CompleteAsync(string code, string state, string iss, CancellationToken cancellationToken = default);
    }
}
