using AMAIS.WKFLO.Common.MD.Domain.Entities.Attributes;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// Everything needed to resume an in-progress MCP OAuth 2.1 + PKCE consent flow from a completely
    /// different process than the one that started it — the persisted replacement for what used to be a
    /// live, suspended C# call stack (a <c>TaskCompletionSource</c> held in an in-process
    /// <c>ConcurrentDictionary</c>) inside <c>McpOAuthConsentService</c>. That in-process design silently
    /// broke as soon as the "start" and "callback" HTTP requests landed on two different replicas (Bug 7129)
    /// — a single SQL row keyed by the OAuth <see cref="State"/> value fixes that: whichever replica's
    /// <c>BeginAsync</c> created the row, any replica's <c>CompleteAsync</c> can read it back.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed by <see cref="State"/> (the OAuth `state` value) rather than a surrogate id — the
    /// callback request carries no id of any kind, only `code`/`state`/`iss` (the redirect_uri is a single
    /// static URL shared by every connection, see <c>McpOAuthCallbackUrlBuilder</c>), so `state` is the only
    /// thing that can look this row back up. This is also the same CSRF property the pre-existing
    /// state-validation fix (commit <c>62260bc2</c>) relies on: an attacker who can't guess a real,
    /// cryptographically random `state` simply finds no row here.
    ///
    /// One row per in-flight flow. <see cref="CodeVerifier"/> and <see cref="ClientSecret"/> are encrypted at
    /// rest via <c>IMcpTokenProtector</c> (the same protector <c>ToolOAuthToken</c> uses) before being
    /// persisted — never read/write these columns directly without going through it. Rows are deleted as soon
    /// as they're consumed (success or failure) by <c>CompleteAsync</c>, and opportunistically by
    /// <c>BeginAsync</c> for anything already past <see cref="ExpiresOnUtc"/> — there is no separate
    /// background cleanup job.
    /// </remarks>
    public class McpOAuthPendingFlow
    {
        /// <summary>The OAuth `state` value generated for this flow. Primary key — see the class remarks.</summary>
        public string State { get; set; }

        /// <summary>The connection this flow belongs to (<c>RunableTool.McpOAuthConnectionId</c>).</summary>
        public Guid ConnectionId { get; set; }

        /// <summary>
        /// The WKFLO user who started this flow (<c>UserContext.Guid</c> at the real, authenticated "start"
        /// endpoint), when the tool is configured for per-user OAuth
        /// (<c>McpServerConfig.OAuthScope == McpOAuthScope.PerUser</c>, e.g. Canva). Null for the ordinary
        /// tenant-shared mode (e.g. Humantic). Never populated from the callback itself — a standard
        /// OAuth2/OIDC callback carries no WKFLO user identity, only `code`/`state`/`iss`.
        /// </summary>
        public Guid? RequestingUserId { get; set; }

        /// <summary>The PKCE (RFC 7636) code_verifier generated for this flow. Encrypted at rest. Also
        /// masked in the audit trail — defense-in-depth, no legitimate reason to expose even the encrypted
        /// blob via GET /api/v1/tenant/audit (same reasoning as ToolOAuthToken.AccessToken).</summary>
        [SensitiveAuditField]
        public string CodeVerifier { get; set; }

        /// <summary>
        /// The OAuth client id used for this flow — either <c>McpServerConfig.ClientId</c> as configured, or
        /// the id issued by Dynamic Client Registration (RFC 7591) when the tool had none configured.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>The OAuth client secret used for this flow, when the client is confidential. Encrypted at
        /// rest. Null for a public client (PKCE-only, no secret). Also masked in the audit trail — see
        /// <see cref="CodeVerifier"/>.</summary>
        [SensitiveAuditField]
        public string ClientSecret { get; set; }

        /// <summary>How <see cref="ClientId"/>/<see cref="ClientSecret"/> are presented at the token endpoint
        /// (e.g. "client_secret_basic", "client_secret_post", "none").</summary>
        public string TokenEndpointAuthMethod { get; set; }

        /// <summary>The authorization server's token endpoint, resolved during discovery in <c>BeginAsync</c>
        /// — <c>CompleteAsync</c> posts the authorization-code exchange here without re-running discovery.</summary>
        public string TokenEndpoint { get; set; }

        /// <summary>
        /// The authorization server's issuer identifier, when discovery found one, for RFC 9207 `iss`
        /// validation on callback. Null when discovery fell back to the MCP-default convention with no
        /// explicit issuer.
        /// </summary>
        public string AuthorizationServerIssuer { get; set; }

        /// <summary>
        /// The RFC 8707 `resource` indicator sent with both the authorization request and the token exchange
        /// — the MCP server's own canonical endpoint URI (<c>McpServerConfig.Endpoint</c>). Also doubles as
        /// the endpoint <c>CompleteAsync</c> connects to for its post-token MCP handshake verification, so no
        /// separate "MCP endpoint" column is needed alongside it.
        /// </summary>
        public string Resource { get; set; }

        /// <summary>Space-delimited OAuth scopes requested for this flow.</summary>
        public string Scope { get; set; }

        /// <summary>The redirect_uri used for this flow (<c>McpServerConfig.RedirectUri</c> — the single
        /// static callback URL shared by every connection).</summary>
        public string RedirectUri { get; set; }

        public DateTimeOffset CreatedOnUtc { get; set; }

        /// <summary>When this row stops being a valid pending flow (<see cref="CreatedOnUtc"/> plus the
        /// consent window, matching the previous design's <c>UserConsentTimeout</c>). An expired row is
        /// treated identically to a missing one by <c>CompleteAsync</c>'s lookup.</summary>
        public DateTimeOffset ExpiresOnUtc { get; set; }
    }
}
