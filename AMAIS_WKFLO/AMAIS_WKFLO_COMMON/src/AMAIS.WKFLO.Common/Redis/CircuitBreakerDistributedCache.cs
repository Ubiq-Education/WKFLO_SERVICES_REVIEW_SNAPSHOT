#nullable enable

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Redis
{
    /// <summary>
    /// HybridCache swallows L2 errors but still waits for each failing call. After a failure this cache behaves as empty
    /// (reads miss, writes are dropped) for <see cref="OpenDuration"/>, so a Redis outage costs a request nothing but the database call.
    /// </summary>
    public sealed class CircuitBreakerDistributedCache(IDistributedCache inner, ILogger<CircuitBreakerDistributedCache> logger) : IDistributedCache
    {
        public static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(30);

        private long _openUntilTicks;

        private bool IsOpen => Environment.TickCount64 < Interlocked.Read(ref _openUntilTicks);

        public byte[]? Get(string key) => Run(() => inner.Get(key), null);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => RunAsync(() => inner.GetAsync(key, token), null);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Run(() => { inner.Set(key, value, options); return 0; }, 0);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            RunAsync(async () => { await inner.SetAsync(key, value, options, token); return 0; }, 0);

        public void Refresh(string key) => Run(() => { inner.Refresh(key); return 0; }, 0);

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            RunAsync(async () => { await inner.RefreshAsync(key, token); return 0; }, 0);

        public void Remove(string key) => Run(() => { inner.Remove(key); return 0; }, 0);

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            RunAsync(async () => { await inner.RemoveAsync(key, token); return 0; }, 0);

        private T Run<T>(Func<T> call, T fallback)
        {
            if (IsOpen)
            {
                return fallback;
            }

            try
            {
                return call();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Open(ex);
                return fallback;
            }
        }

        private async Task<T> RunAsync<T>(Func<Task<T>> call, T fallback)
        {
            if (IsOpen)
            {
                return fallback;
            }

            try
            {
                return await call();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Open(ex);
                return fallback;
            }
        }

        private void Open(Exception ex)
        {
            var openUntil = Environment.TickCount64 + (long)OpenDuration.TotalMilliseconds;

            // Concurrent failures open it once; log only for the one that did.
            var previous = Interlocked.Exchange(ref _openUntilTicks, openUntil);

            if (Environment.TickCount64 >= previous)
            {
                logger.LogWarning(ex, "Redis cache call failed; skipping the distributed cache for {Duration}.", OpenDuration);
            }
        }
    }
}
