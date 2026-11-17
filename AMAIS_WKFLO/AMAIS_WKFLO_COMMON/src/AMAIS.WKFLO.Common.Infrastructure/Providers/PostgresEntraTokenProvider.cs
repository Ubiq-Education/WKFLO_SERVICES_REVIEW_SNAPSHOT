using Azure.Core;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    /// <summary>
    /// Supplies Microsoft Entra access tokens as the PostgreSQL password, caching each token until shortly
    /// before its own <see cref="AccessToken.ExpiresOn"/> (or <see cref="AccessToken.RefreshOn"/>, if earlier).
    /// "Shortly before" is the refresh margin (5 minutes by default), capped at half the lifetime the token
    /// had left when it was fetched, so short-lived tokens are still cached for a while.
    /// </summary>
    /// <remarks>
    /// Npgsql calls the password provider only when it opens a new physical connection, so pooled connections
    /// are unaffected. Unlike a fixed refresh timer, the cache lifetime follows the token actually returned: a
    /// credential may hand back an already-cached token with far less than its nominal lifetime left (typical of
    /// the Visual Studio / Azure CLI credentials used locally), which a fixed 55-minute timer would keep serving
    /// after it expired. The token value itself is never logged.
    /// </remarks>
    internal sealed class PostgresEntraTokenProvider
    {
        /// <summary>The Entra scope for Azure Database for PostgreSQL.</summary>
        internal static readonly TokenRequestContext TokenRequestContext =
            new(["https://ossrdbms-aad.database.windows.net/.default"]);

        /// <summary>
        /// How long before a token expires it is treated as stale and replaced. Capped at half the token's
        /// remaining lifetime at fetch time.
        /// </summary>
        internal static readonly TimeSpan DefaultRefreshMargin = TimeSpan.FromMinutes(5);

        private readonly TokenCredential _credential;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _refreshMargin;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private volatile CachedToken _cachedToken;

        public PostgresEntraTokenProvider(TokenCredential credential, TimeProvider timeProvider, ILogger logger, TimeSpan? refreshMargin = null)
        {
            ArgumentNullException.ThrowIfNull(credential);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(logger);

            _credential = credential;
            _timeProvider = timeProvider;
            _logger = logger;
            _refreshMargin = refreshMargin ?? DefaultRefreshMargin;
        }

        /// <summary>
        /// Returns a valid access token, fetching a new one only when the cached one is missing or close to expiry.
        /// Concurrent callers share a single fetch. If the credential throws, the exception propagates, nothing is
        /// cached and the next call tries again.
        /// </summary>
        public async ValueTask<string> GetPasswordAsync(CancellationToken cancellationToken)
        {
            if (TryGetFreshToken(out var token))
                return token;

            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (TryGetFreshToken(out token))
                    return token;

                var accessToken = await _credential.GetTokenAsync(TokenRequestContext, cancellationToken).ConfigureAwait(false);
                return Store(accessToken);
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>
        /// Synchronous counterpart of <see cref="GetPasswordAsync"/>, used by Npgsql for <c>NpgsqlConnection.Open()</c>.
        /// Uses the credential's synchronous API rather than blocking on the async one.
        /// </summary>
        public string GetPassword()
        {
            if (TryGetFreshToken(out var token))
                return token;

            _refreshLock.Wait();
            try
            {
                if (TryGetFreshToken(out token))
                    return token;

                var accessToken = _credential.GetToken(TokenRequestContext, CancellationToken.None);
                return Store(accessToken);
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private bool TryGetFreshToken(out string token)
        {
            var cached = _cachedToken;
            if (cached is not null && _timeProvider.GetUtcNow() < cached.RefreshAt)
            {
                token = cached.Token;
                return true;
            }

            token = null;
            return false;
        }

        /// <summary>
        /// Caches the token until <c>ExpiresOn − min(margin, remaining / 2)</c>, where <c>remaining</c> is the token's
        /// lifetime left at fetch time, or until <see cref="AccessToken.RefreshOn"/> if that is earlier. Capping the
        /// margin at half the remaining lifetime keeps a token that arrives with less than the margin left cached for
        /// part of its life, instead of refetching it on every new physical connection. An already-expired token is
        /// returned but not cached.
        /// </summary>
        private string Store(AccessToken accessToken)
        {
            var remaining = accessToken.ExpiresOn - _timeProvider.GetUtcNow();
            var halfRemaining = remaining > TimeSpan.Zero ? remaining / 2 : TimeSpan.Zero;
            var margin = halfRemaining < _refreshMargin ? halfRemaining : _refreshMargin;

            var refreshAt = accessToken.ExpiresOn - margin;
            if (accessToken.RefreshOn is { } refreshOn && refreshOn < refreshAt)
                refreshAt = refreshOn;

            _cachedToken = new CachedToken(accessToken.Token, refreshAt);

            _logger.LogDebug(
                "Fetched a new PostgreSQL Entra access token expiring at {ExpiresOn}; it will be refreshed after {RefreshAt}",
                accessToken.ExpiresOn,
                refreshAt);

            return accessToken.Token;
        }

        private sealed record CachedToken(string Token, DateTimeOffset RefreshAt);
    }
}
