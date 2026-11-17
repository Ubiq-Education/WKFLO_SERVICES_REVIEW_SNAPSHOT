using AMAIS.WKFLO.Common.MD.Domain.Entities.Attributes;
using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// Persists the OAuth 2.1 tokens obtained for one MCP OAuth2 connection (e.g. a tenant's connection to
    /// Humantic's MCP server), keyed by <see cref="McpOAuthConnectionId"/> rather than a specific
    /// <see cref="Tool"/> — a <c>TenantTool</c> and its <c>PublishedTenantTool</c> share the same connection
    /// id (see <see cref="RunableTool.McpOAuthConnectionId"/>) and therefore the same row here, since
    /// publishing doesn't create a second, independent OAuth grant. Kept separate from <see cref="Tool.Config"/>
    /// so a live token refresh never collides with an admin editing the tool's JSON config. There is at most
    /// one row per <see cref="McpOAuthConnectionId"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="AccessToken"/>, <see cref="RefreshToken"/> and <see cref="ClientSecret"/> are encrypted at
    /// rest via <c>AMAIS.WKFLO.Common.MD.Domain.Security.IMcpTokenProtector</c> before being persisted; never
    /// read/write these columns directly without going through the protector.
    /// </remarks>
    public class ToolOAuthToken : Entity<int>
    {
        public Guid McpOAuthConnectionId { get; set; }

        /// <summary>
        /// The WKFLO user this token belongs to, when the owning tool is configured for per-user OAuth (see
        /// <c>McpServerConfig.OAuthScope == McpOAuthScope.PerUser</c>) — e.g. Canva, which has no tenant/org-shared
        /// mode at all. Null for a tenant-shared connection (the default, and the only mode Humantic supports).
        /// Shared-vs-per-user is a fixed choice per tool, meant never to mix for the same
        /// <see cref="McpOAuthConnectionId"/> — but the two filtered unique indexes only enforce "at most one
        /// shared row" and "at most one row per user" independently, NOT that both kinds can't coexist for the
        /// same connection (e.g. left over after a tool's mode is switched); that would need a database-level
        /// mechanism this schema doesn't have. The real operational guarantee lives in application code:
        /// <c>McpOAuthScopeResolver</c> never resolves a PerUser tool's requesting-user id to null, so its
        /// reads/writes can never accidentally hit a stray shared row even if one exists. See
        /// <see cref="Data.Config.ToolOAuthTokenConfiguration"/> for the indexes themselves.
        /// </summary>
        public Guid? UserId { get; set; }

        /// <summary>Encrypted at rest. Also masked in the audit trail — defense-in-depth, since there's no
        /// legitimate reason to expose even the encrypted blob via GET /api/v1/tenant/audit.</summary>
        [SensitiveAuditField]
        public string AccessToken { get; set; }

        /// <summary>Encrypted at rest. Null when the authorization server did not issue a refresh token. Also
        /// masked in the audit trail — see <see cref="AccessToken"/>.</summary>
        [SensitiveAuditField]
        public string RefreshToken { get; set; }

        public string TokenType { get; set; }

        /// <summary>Null when the authorization server did not report an expiry (token treated as non-expiring).</summary>
        public DateTimeOffset? ExpiresAtUtc { get; set; }

        public string Scope { get; set; }

        /// <summary>Client id used to obtain this token — dynamically registered (RFC 7591) or preconfigured on the tool.</summary>
        public string ClientId { get; set; }

        /// <summary>Encrypted at rest. Only populated when dynamic client registration returned a confidential-client secret.
        /// Also masked in the audit trail — see <see cref="AccessToken"/>.</summary>
        [SensitiveAuditField]
        public string ClientSecret { get; set; }

        public string TokenEndpointAuthMethod { get; set; }

        public string AuthorizationServer { get; set; }

        /// <summary>When the tenant first completed the consent flow for this tool. Never updated after creation
        /// so the OAuth status endpoint can show a stable "connected since" time even across silent refreshes.</summary>
        public DateTimeOffset ConnectedOnUtc { get; set; }

        /// <summary>When <see cref="AccessToken"/> was (re)issued — updated on every silent refresh, used together
        /// with <see cref="ExpiresAtUtc"/> to know when the next refresh is due.</summary>
        public DateTimeOffset TokenObtainedAtUtc { get; set; }

        /// <summary>
        /// When a real connection attempt (the MCP handshake — <c>McpClient.CreateAsync</c> — not just the
        /// OAuth token exchange) against this tool was last recorded. Null when a token was saved but no
        /// connection attempt has been recorded yet (e.g. rows written before this column existed).
        /// </summary>
        public DateTimeOffset? LastCheckedOnUtc { get; set; }

        /// <summary>
        /// Outcome of the connection attempt at <see cref="LastCheckedOnUtc"/>. The OAuth token exchange
        /// succeeding is not proof the MCP connection itself works (e.g. Humantic can return a 401
        /// "no active subscription" business error from a fully valid token) — this reflects the real
        /// handshake result. Null means "token saved but no outcome recorded yet"; treated the same as
        /// <see langword="true"/> by <c>GetToolMcpOAuthStatusQueryHandler</c> so existing rows (and the brief
        /// window between a token being saved and the following handshake completing) still read as
        /// "Connected" rather than incorrectly flagging an issue that hasn't actually been observed.
        /// </summary>
        public bool? LastCheckSucceeded { get; set; }

        /// <summary>The connection failure's message when <see cref="LastCheckSucceeded"/> is <see langword="false"/>; null otherwise.</summary>
        public string LastError { get; set; }
    }
}
