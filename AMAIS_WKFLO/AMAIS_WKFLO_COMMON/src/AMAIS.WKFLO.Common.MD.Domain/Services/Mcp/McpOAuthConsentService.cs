using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth;
using AMAIS.WKFLO.Common.Models.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// See <see cref="IMcpOAuthConsentService"/>.
    /// </summary>
    /// <remarks>
    /// This is a from-scratch, hand-rolled OAuth 2.1 + PKCE (RFC 7636) + Dynamic Client Registration
    /// (RFC 7591) implementation using a plain <see cref="HttpClient"/> — it deliberately does NOT use the
    /// ModelContextProtocol SDK's interactive OAuth machinery
    /// (<c>ClientOAuthOptions.AuthorizationCallbackHandler</c>/its internal <c>ClientOAuthProvider</c>) for
    /// the "start"/"callback" leg at all. That SDK machinery models interactive consent as a single blocking
    /// callback invoked mid-connection and expected to return the authorization code synchronously — which
    /// only works by keeping a live, suspended C# call stack (with the PKCE `code_verifier` as a local
    /// variable inside it, never exposed on any public type) alive in process memory between "start" and
    /// "callback". That's what made the previous design silently break as soon as those two HTTP requests
    /// landed on two different replicas (Bug 7129) — Azure Container Apps' HTTP-concurrency autoscaling means
    /// they already can. Every piece of state that needs to survive the gap is instead a single
    /// <see cref="McpOAuthPendingFlow"/> SQL row keyed by the OAuth `state` value, so <see cref="BeginAsync"/>
    /// and <see cref="CompleteAsync"/> can run on entirely independent processes with nothing shared in
    /// memory. The SDK's OAuth machinery is still used for exactly one thing: a single bounded
    /// <see cref="McpClient.CreateAsync(ModelContextProtocol.Client.IClientTransport, ModelContextProtocol.Client.McpClientOptions, Microsoft.Extensions.Logging.ILoggerFactory, CancellationToken)"/>
    /// call in <see cref="CompleteAsync"/>, to preserve today's "does the MCP handshake actually succeed with
    /// this token" check — see that method's remarks.
    /// </remarks>
    public class McpOAuthConsentService : IMcpOAuthConsentService
    {
        // Matches the previous design's UserConsentTimeout — how long a tenant has to complete the browser
        // consent step before the pending flow is treated as abandoned.
        internal static readonly TimeSpan PendingFlowLifetime = TimeSpan.FromMinutes(10);

        // Bounded — this is now a single post-token handshake verification, not an indefinite cross-request
        // wait (the previous design's ConnectionTimeout of 300s was sized for a human to complete consent
        // *during* this call; that no longer happens here at all).
        private static readonly TimeSpan HandshakeVerificationTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _httpClient;
        private readonly IMasterDataDbContext _db;
        private readonly IMcpOAuthTokenStore _tokenStore;
        private readonly IMcpTokenProtector _protector;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<McpOAuthConsentService> _logger;

        public McpOAuthConsentService(
            HttpClient httpClient,
            IMasterDataDbContext db,
            IMcpOAuthTokenStore tokenStore,
            IMcpTokenProtector protector,
            IDateTimeProvider dateTimeProvider,
            ILogger<McpOAuthConsentService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
            _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Uri> BeginAsync(McpServerConfig serverConfig, Guid connectionId, Guid? requestingUserId = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(serverConfig);
            if (serverConfig.AuthType != McpAuthType.OAuth2)
                throw new InvalidOperationException("Interactive OAuth consent requires McpServerConfig.AuthType = OAuth2.");
            if (string.IsNullOrWhiteSpace(serverConfig.RedirectUri))
                throw new InvalidOperationException("McpServerConfig.RedirectUri must be resolved before starting the OAuth consent flow.");

            // Opportunistic cleanup — no dedicated background job (see McpOAuthPendingFlow's remarks).
            await DeleteExpiredPendingFlowsAsync(cancellationToken);

            var discovery = await McpOAuthDiscovery.DiscoverAsync(_httpClient, serverConfig.Endpoint, cancellationToken);

            // Resolved once, before DCR, and reused for both the registration request and the authorize URL —
            // registering a client for one scope set and then asking it to authorize a different one is at
            // best inconsistent and at worst rejected by an authorization server that enforces registered
            // scopes (Copilot PR review, PR 16245: these previously diverged — DCR used only the tool's own
            // configured scopes with no fallback, while the authorize URL also fell back to the server's
            // resource-advertised scopes when the config had none).
            var scope = string.Join(' ', (serverConfig.Scopes?.Length ?? 0) > 0 ? serverConfig.Scopes : (discovery.ScopesSupported ?? []));

            string clientId = serverConfig.ClientId;
            // Stored encrypted (enc:v1:) since US 7849; legacy plaintext values pass through unchanged.
            string clientSecret = McpClientSecretHelper.Resolve(_protector, serverConfig.ClientSecret);
            string tokenEndpointAuthMethod;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                if (string.IsNullOrWhiteSpace(discovery.RegistrationEndpoint))
                    throw new InvalidOperationException(
                        $"MCP server '{serverConfig.Name}' has no ClientId configured and its authorization server doesn't advertise a Dynamic Client Registration endpoint.");

                var registration = await McpOAuthDynamicClientRegistration.RegisterAsync(
                    _httpClient, discovery.RegistrationEndpoint, serverConfig.RedirectUri, serverConfig.Name,
                    scope, cancellationToken);

                clientId = registration.ClientId;
                clientSecret = registration.ClientSecret;
                tokenEndpointAuthMethod = registration.TokenEndpointAuthMethod;
            }
            else
            {
                tokenEndpointAuthMethod = string.IsNullOrWhiteSpace(clientSecret)
                    ? McpOAuthTokenEndpointAuthMethods.None
                    : McpOAuthTokenEndpointAuthMethods.ClientSecretPost;
            }

            var codeVerifier = PkceGenerator.GenerateCodeVerifier();
            var codeChallenge = PkceGenerator.ComputeCodeChallenge(codeVerifier);
            var state = PkceGenerator.GenerateState();
            var now = _dateTimeProvider.DateTimeOffsetUtcNow();

            _db.McpOAuthPendingFlows.Add(new McpOAuthPendingFlow
            {
                State = state,
                ConnectionId = connectionId,
                RequestingUserId = requestingUserId,
                CodeVerifier = _protector.Protect(codeVerifier),
                ClientId = clientId,
                ClientSecret = _protector.Protect(clientSecret),
                TokenEndpointAuthMethod = tokenEndpointAuthMethod,
                TokenEndpoint = discovery.TokenEndpoint,
                AuthorizationServerIssuer = discovery.Issuer,
                // RFC 8707's resource indicator for an MCP server is its own canonical endpoint URI — always
                // sent, per the MCP Authorization spec, and doubles as the endpoint CompleteAsync's handshake
                // verification connects to (see McpOAuthPendingFlow.Resource's remarks).
                Resource = serverConfig.Endpoint,
                Scope = scope,
                RedirectUri = serverConfig.RedirectUri,
                CreatedOnUtc = now,
                ExpiresOnUtc = now.Add(PendingFlowLifetime)
            });
            await _db.SaveChangesAsync(cancellationToken);

            return BuildAuthorizationUri(discovery.AuthorizationEndpoint, clientId, serverConfig.RedirectUri, scope, state, codeChallenge, serverConfig.Endpoint);
        }

        public async Task<bool> CompleteAsync(string code, string state, string iss, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(state))
            {
                _logger.LogWarning("MCP OAuth callback with no `state`; rejecting.");
                return false;
            }

            var now = _dateTimeProvider.DateTimeOffsetUtcNow();

            // `state` is the entire lookup key — a stronger CSRF property than a route-supplied connection
            // id ever was (commit 62260bc2): an attacker has nothing to guess against except a
            // cryptographically random value a real BeginAsync call generated. Expired rows are treated
            // identically to missing ones.
            var flow = await _db.McpOAuthPendingFlows
                .Where(f => f.State == state && f.ExpiresOnUtc >= now)
                .FirstOrDefaultAsync(cancellationToken);

            if (flow == null)
            {
                _logger.LogWarning("MCP OAuth callback with no matching (or expired) pending flow for the given state; rejecting.");
                return false;
            }

            try
            {
                // RFC 9207: when both sides carry an issuer, they must agree — otherwise this could be an
                // authorization-server mix-up attack.
                if (!string.IsNullOrEmpty(iss) && !string.IsNullOrEmpty(flow.AuthorizationServerIssuer) &&
                    !string.Equals(iss, flow.AuthorizationServerIssuer, StringComparison.Ordinal))
                {
                    _logger.LogWarning("MCP OAuth callback `iss` mismatch for connection {ConnectionId}; rejecting.", flow.ConnectionId);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(code))
                    return false;

                var clientSecret = _protector.Unprotect(flow.ClientSecret);

                var tokenResponse = await McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                    _httpClient,
                    flow.TokenEndpoint,
                    code,
                    flow.RedirectUri,
                    _protector.Unprotect(flow.CodeVerifier),
                    flow.ClientId,
                    clientSecret,
                    flow.TokenEndpointAuthMethod,
                    flow.Resource,
                    cancellationToken);

                // The tool's client id/endpoint may have changed while consent was in flight (its update deletes
                // pending flows, but this callback may already have loaded this one). Never save a token issued
                // to an OAuth client the tool no longer uses.
                if (!await FlowMatchesCurrentToolConfigAsync(flow, cancellationToken))
                {
                    _logger.LogWarning(
                        "MCP OAuth callback for connection {ConnectionId}: the tool's client id or endpoint changed since consent started; discarding the token.",
                        flow.ConnectionId);
                    return false;
                }

                await _tokenStore.SaveAsync(flow.ConnectionId, new McpOAuthTokenData
                {
                    TokenType = tokenResponse.TokenType,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn.HasValue ? TimeSpan.FromSeconds(tokenResponse.ExpiresIn.Value) : null,
                    Scope = tokenResponse.Scope ?? flow.Scope,
                    ObtainedAt = now,
                    ClientId = flow.ClientId,
                    ClientSecret = clientSecret,
                    TokenEndpointAuthMethod = flow.TokenEndpointAuthMethod,
                    AuthorizationServer = flow.AuthorizationServerIssuer
                }, flow.RequestingUserId, cancellationToken);

                var (handshakeSucceeded, handshakeError) = await VerifyMcpHandshakeAsync(flow, cancellationToken);

                await _tokenStore.RecordConnectionResultAsync(flow.ConnectionId, handshakeSucceeded, handshakeError, flow.RequestingUserId, cancellationToken);

                return handshakeSucceeded;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OAuth token exchange failed for MCP connection {ConnectionId}", flow.ConnectionId);
                // Best-effort — a no-op if the token exchange failed before any row was ever saved (nothing
                // to annotate yet), matching RecordConnectionResultAsync's own contract.
                await _tokenStore.RecordConnectionResultAsync(flow.ConnectionId, succeeded: false, error: ex.Message, flow.RequestingUserId, CancellationToken.None);
                return false;
            }
            finally
            {
                // One-shot: consumed here whether the flow succeeded or failed.
                _db.McpOAuthPendingFlows.Remove(flow);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// True when at least one live tool on the flow's connection — the tenant tool or its published copy,
        /// which share the connection id and can each start consent — is still configured for the flow's
        /// client id and endpoint (see <see cref="McpClientSecretHelper.MatchesOAuthClient"/>). False when the
        /// tool was deleted. A secret-only change isn't detectable here (the flow doesn't keep it for
        /// comparison); the tool update's pending-flow deletion covers that case.
        /// </summary>
        private async Task<bool> FlowMatchesCurrentToolConfigAsync(McpOAuthPendingFlow flow, CancellationToken cancellationToken)
        {
            var configs = await _db.TenantTools.AsNoTracking()
                .Where(t => t.McpOAuthConnectionId == flow.ConnectionId)
                .Select(t => t.Config)
                .ToListAsync(cancellationToken);
            configs.AddRange(await _db.PublishedTenantTools.AsNoTracking()
                .Where(t => t.McpOAuthConnectionId == flow.ConnectionId)
                .Select(t => t.Config)
                .ToListAsync(cancellationToken));

            return configs.Any(config => McpClientSecretHelper.MatchesOAuthClient(config, flow.ClientId, flow.Resource));
        }

        /// <summary>
        /// Preserves today's post-token "does the MCP handshake actually work" check (the thing that
        /// produces the `ConnectionIssue` status, e.g. Humantic's "No active subscription") via one bounded
        /// <see cref="McpClient.CreateAsync(ModelContextProtocol.Client.IClientTransport, ModelContextProtocol.Client.McpClientOptions, Microsoft.Extensions.Logging.ILoggerFactory, CancellationToken)"/>
        /// call, using <see cref="McpOAuthTokenCacheAdapter"/> pre-loaded with the token
        /// <see cref="CompleteAsync"/> just saved. Per the SDK, <c>ITokenCache.GetTokensAsync</c> is
        /// consulted before any interactive step, so a valid cached token means
        /// <c>AuthorizationCallbackHandler</c> never fires — it's set to throw defensively; it should be
        /// unreachable here.
        /// </summary>
        private async Task<(bool Succeeded, string Error)> VerifyMcpHandshakeAsync(McpOAuthPendingFlow flow, CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(HandshakeVerificationTimeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                // A fresh transport per attempt: McpClientConnector may retry with a pinned protocol version
                // (e.g. Canva answers the default server/discover probe with HTTP 500).
                IClientTransport CreateTransport() => new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(flow.Resource),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    EnableStandaloneGetStream = false,
                    OAuth = new ClientOAuthOptions
                    {
                        ClientId = flow.ClientId,
                        ClientSecret = string.IsNullOrWhiteSpace(flow.ClientSecret) ? null : _protector.Unprotect(flow.ClientSecret),
                        Scopes = string.IsNullOrWhiteSpace(flow.Scope) ? [] : flow.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                        RedirectUri = new Uri(flow.RedirectUri),
                        TokenCache = new McpOAuthTokenCacheAdapter(_tokenStore, flow.ConnectionId, flow.RequestingUserId),
                        AuthorizationCallbackHandler = (_, _) => throw new InvalidOperationException(
                            "Unexpected interactive re-authorization during post-token MCP handshake verification; " +
                            "the just-saved token should always be used from cache.")
                    },
                    ConnectionTimeout = HandshakeVerificationTimeout
                });

                await using var client = await McpClientConnector.ConnectAsync(CreateTransport, null, _logger, flow.Resource, combinedCts.Token);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP handshake verification failed after OAuth token exchange for connection {ConnectionId}", flow.ConnectionId);
                return (false, ex.Message);
            }
        }

        private async Task DeleteExpiredPendingFlowsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var now = _dateTimeProvider.DateTimeOffsetUtcNow();
                var expired = await _db.McpOAuthPendingFlows
                    .Where(f => f.ExpiresOnUtc < now)
                    .ToListAsync(cancellationToken);

                if (expired.Count == 0)
                    return;

                foreach (var flow in expired)
                    _db.McpOAuthPendingFlows.Remove(flow);

                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Best-effort — never let a cleanup failure block starting a brand-new flow.
                _logger.LogWarning(ex, "Failed to opportunistically delete expired MCP OAuth pending flows.");
            }
        }

        private static Uri BuildAuthorizationUri(string authorizationEndpoint, string clientId, string redirectUri, string scope, string state, string codeChallenge, string resource)
        {
            var builder = new UriBuilder(authorizationEndpoint);
            var query = System.Web.HttpUtility.ParseQueryString(builder.Query);
            query["response_type"] = "code";
            query["client_id"] = clientId;
            query["redirect_uri"] = redirectUri;
            if (!string.IsNullOrWhiteSpace(scope))
                query["scope"] = scope;
            query["state"] = state;
            query["code_challenge"] = codeChallenge;
            query["code_challenge_method"] = "S256";
            if (!string.IsNullOrWhiteSpace(resource))
                query["resource"] = resource;
            builder.Query = query.ToString();
            return builder.Uri;
        }
    }
}
