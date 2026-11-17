#nullable enable

using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;

namespace AMAIS.WKFLO.Common.Redis
{
    /// <summary>
    /// Owns the single <see cref="IConnectionMultiplexer"/> of the process, shared by the distributed cache (HybridCache L2)
    /// and the cache invalidation broadcast. The connection is created lazily; a failed attempt is kept for a while
    /// so that a Redis outage does not turn every request into a new connection attempt.
    /// </summary>
    public sealed class RedisConnectionProvider(RedisSettings settings) : IAsyncDisposable, IDisposable
    {
        // The EF cache provider blocks synchronously on cache calls, so Redis must fail fast instead of stalling requests.
        private const int ConnectTimeoutMs = 2000;
        private const int CommandTimeoutMs = 1000;

        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

        private readonly object _lock = new();
        private Task<IConnectionMultiplexer>? _connection;
        private long _retryAtTicks;

        public Task<IConnectionMultiplexer> GetConnectionAsync()
        {
            lock (_lock)
            {
                var failed = _connection is { IsFaulted: true } or { IsCanceled: true };

                if (_connection is null || (failed && Environment.TickCount64 >= _retryAtTicks))
                {
                    _connection = ConnectAsync();
                    _retryAtTicks = Environment.TickCount64 + (long)RetryAfterFailure.TotalMilliseconds;
                }

                return _connection;
            }
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            Task<IConnectionMultiplexer>? connection;
            lock (_lock)
            {
                connection = _connection;
                _connection = null;
            }

            if (connection is { IsCompletedSuccessfully: true })
            {
                await connection.Result.DisposeAsync();
            }
        }

        private async Task<IConnectionMultiplexer> ConnectAsync()
        {
            ConfigurationOptions options;

            if (settings.ConnectionString is not null)
            {
                options = ConfigurationOptions.Parse(settings.ConnectionString);
            }
            else
            {
                options = ConfigurationOptions.Parse(settings.AzureEndpoint
                    ?? throw new InvalidOperationException("Redis is not configured."));

                if (settings.AzureClientId is not null)
                {
                    await options.ConfigureForAzureWithUserAssignedManagedIdentityAsync(settings.AzureClientId);
                }
                else
                {
                    await options.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());
                }
            }

            // Redis being down must never take the host down: keep retrying in the background instead of failing the connect,
            // and fail commands immediately while disconnected instead of queueing them until they time out.
            options.AbortOnConnectFail = false;
            options.BacklogPolicy = BacklogPolicy.FailFast;
            options.ConnectTimeout = ConnectTimeoutMs;
            options.SyncTimeout = CommandTimeoutMs;
            options.AsyncTimeout = CommandTimeoutMs;

            return await ConnectionMultiplexer.ConnectAsync(options);
        }
    }
}
