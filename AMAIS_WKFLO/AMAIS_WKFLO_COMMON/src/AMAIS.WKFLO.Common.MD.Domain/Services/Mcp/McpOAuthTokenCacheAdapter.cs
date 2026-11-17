using ModelContextProtocol.Authentication;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// Bridges the ModelContextProtocol SDK's <see cref="ITokenCache"/> (used by <c>ClientOAuthOptions.TokenCache</c>)
    /// to our own <see cref="IMcpOAuthTokenStore"/>, so the SDK's cached/silently-refreshed token path reads
    /// and writes through the same persisted store both the AI API's chat-time tool loading
    /// (<c>McpClientService</c>) and the MD API's OAuth consent flow (<c>McpOAuthConsentService</c>'s
    /// post-token handshake verification) use. Kept out of <see cref="IMcpOAuthTokenStore"/> itself so that
    /// interface never has to depend on the MCP SDK package.
    /// </summary>
    public class McpOAuthTokenCacheAdapter : ITokenCache
    {
        private readonly IMcpOAuthTokenStore _store;
        private readonly Guid _connectionId;
        private readonly Guid? _userId;

        /// <summary><paramref name="userId"/> is the WKFLO user to scope reads/writes to for a per-user
        /// OAuth tool (<c>McpServerConfig.OAuthScope == McpOAuthScope.PerUser</c> — e.g. Canva); left null for
        /// the ordinary tenant-shared mode (e.g. Humantic).</summary>
        public McpOAuthTokenCacheAdapter(IMcpOAuthTokenStore store, Guid connectionId, Guid? userId = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _connectionId = connectionId;
            _userId = userId;
        }

        public async ValueTask<TokenContainer> GetTokensAsync(CancellationToken cancellationToken)
        {
            var data = await _store.GetAsync(_connectionId, _userId, cancellationToken);
            if (data == null)
                return null;

            return new TokenContainer
            {
                TokenType = data.TokenType,
                AccessToken = data.AccessToken,
                RefreshToken = data.RefreshToken,
                ExpiresIn = data.ExpiresIn.HasValue ? (int)data.ExpiresIn.Value.TotalSeconds : null,
                Scope = data.Scope,
                ObtainedAt = data.ObtainedAt,
                ClientId = data.ClientId,
                ClientSecret = data.ClientSecret,
                TokenEndpointAuthMethod = data.TokenEndpointAuthMethod,
                AuthorizationServer = data.AuthorizationServer
            };
        }

        public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tokens);

            await _store.SaveAsync(_connectionId, new McpOAuthTokenData
            {
                TokenType = tokens.TokenType,
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                ExpiresIn = tokens.ExpiresIn.HasValue ? TimeSpan.FromSeconds(tokens.ExpiresIn.Value) : null,
                Scope = tokens.Scope,
                ObtainedAt = tokens.ObtainedAt,
                ClientId = tokens.ClientId,
                ClientSecret = tokens.ClientSecret,
                TokenEndpointAuthMethod = tokens.TokenEndpointAuthMethod,
                AuthorizationServer = tokens.AuthorizationServer
            }, _userId, cancellationToken);
        }
    }
}
