namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// Pluggable persistence for OAuth tokens obtained when connecting to an OAuth2-protected MCP server,
    /// keyed by <c>RunableTool.McpOAuthConnectionId</c> — a stable identity shared by a <c>TenantTool</c> and
    /// its <c>PublishedTenantTool</c> (see that field's remarks), not by any single <c>Tool.Id</c>. Deliberately
    /// independent of both the ModelContextProtocol SDK's own <c>ITokenCache</c> interface and of
    /// <c>AMAIS.WKFLO.Common.Models.Config.McpServerConfig.Id</c> — that field is purely <c>McpClientService</c>'s
    /// own multi-server registry key (so a single process can address one of several configured servers) and
    /// carries no relationship to which connection a config came from. Both the AI API's silent/background
    /// token-refresh path (<c>McpClientService</c>) and the MD API's interactive OAuth consent endpoints share
    /// this one store, passing the connection id they already have in hand.
    /// </summary>
    public interface IMcpOAuthTokenStore
    {
        /// <summary>
        /// <paramref name="userId"/> selects which row to operate on for a connection whose tool is configured
        /// for per-user OAuth (<c>McpServerConfig.OAuthScope == McpOAuthScope.PerUser</c> — e.g. Canva): pass
        /// the WKFLO user id to read/write that user's own row. Left null (the default) for the ordinary
        /// tenant-shared mode (e.g. Humantic) — every existing caller that never mentions users keeps working
        /// unchanged. Never pass a non-null value for a Shared-mode tool, and vice versa: shared-vs-per-user
        /// is a fixed, mutually exclusive choice per tool, never both for the same connection.
        /// </summary>
        Task<McpOAuthTokenData> GetAsync(Guid connectionId, Guid? userId = null, CancellationToken cancellationToken = default);

        /// <summary>Creates or replaces the token record for <paramref name="connectionId"/> (and, for a
        /// per-user tool, <paramref name="userId"/>). See <see cref="GetAsync"/>'s remarks on <paramref name="userId"/>.</summary>
        Task SaveAsync(Guid connectionId, McpOAuthTokenData token, Guid? userId = null, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid connectionId, Guid? userId = null, CancellationToken cancellationToken = default);

        Task<bool> ExistsAsync(Guid connectionId, Guid? userId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Records the outcome of a real connection attempt (the MCP handshake — <c>McpClient.CreateAsync</c>
        /// completing — not just the OAuth token exchange) for the token row belonging to <paramref name="connectionId"/>
        /// (and, for a per-user tool, <paramref name="userId"/> — see <see cref="GetAsync"/>'s remarks).
        /// A no-op if no row exists yet (nothing to annotate — the tool stays "not connected" from the
        /// caller's point of view). Distinct from token persistence because the SDK writes the
        /// <see cref="McpOAuthTokenData"/> row as soon as the OAuth exchange succeeds — before the MCP
        /// handshake that follows it is known to have worked — so a token row existing is not by itself
        /// proof the connection actually works.
        /// </summary>
        Task RecordConnectionResultAsync(Guid connectionId, bool succeeded, string error, Guid? userId = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Plain (unencrypted, in-memory) shape of an MCP OAuth token exchange, mirroring the fields the
    /// ModelContextProtocol SDK's <c>TokenContainer</c> carries. <see cref="IMcpOAuthTokenStore"/>
    /// implementations are responsible for encrypting/decrypting the secret fields at the storage boundary.
    /// </summary>
    public class McpOAuthTokenData
    {
        public string TokenType { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public TimeSpan? ExpiresIn { get; set; }
        public string Scope { get; set; }
        public DateTimeOffset ObtainedAt { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string TokenEndpointAuthMethod { get; set; }
        public string AuthorizationServer { get; set; }
    }
}
