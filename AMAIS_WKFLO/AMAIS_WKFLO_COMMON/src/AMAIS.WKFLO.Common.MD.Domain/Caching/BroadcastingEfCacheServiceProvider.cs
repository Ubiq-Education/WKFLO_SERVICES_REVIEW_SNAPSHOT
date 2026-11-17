#nullable enable

using System.Text.Json;
using EFCoreSecondLevelCacheInterceptor;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>
    /// Decorates the EF cache provider so invalidations also reach the other processes.
    /// HybridCache only learns about a tag invalidation made by another process once, so without this
    /// a write in one service would leave the L1 of every other replica stale until the entries expire.
    /// A write's tags are invalidated locally at once (as the library does) but announced to the other processes only after
    /// the transaction committed (see <see cref="EfCacheInvalidationInterceptor"/>); writes outside a transaction or SaveChanges are announced at once.
    /// Cache and broadcast failures are logged and never thrown: a write must not fail because the cache is down.
    /// </summary>
    public sealed class BroadcastingEfCacheServiceProvider(
        IEFCacheServiceProvider inner,
        HybridCache hybridCache,
        IEfCacheInvalidationTransport transport,
        EfCacheInstanceId instanceId,
        PendingEfCacheInvalidations pending,
        ILogger<BroadcastingEfCacheServiceProvider> logger) : IEFCacheServiceProvider
    {
        /// <summary>
        /// Same read as the library's provider, but tagged: an entry that HybridCache loads from L2 only gets the tags
        /// passed here in L1, so without them <c>RemoveByTag</c> would never expire it.
        /// </summary>
        public EFCachedData? GetValue(EFCacheKey cacheKey, EFCachePolicy cachePolicy)
        {
            ArgumentNullException.ThrowIfNull(cacheKey);

            return hybridCache
                .GetOrCreateAsync(cacheKey.KeyHash, _ => ValueTask.FromResult<EFCachedData?>(null), options: null, tags: cacheKey.CacheDependencies)
                .Preserve()
                .GetAwaiter()
                .GetResult();
        }

        public void InsertValue(EFCacheKey cacheKey, EFCachedData? value, EFCachePolicy cachePolicy) =>
            inner.InsertValue(cacheKey, value, cachePolicy);

        public void InvalidateCacheDependencies(EFCacheKey cacheKey)
        {
            InvalidateLocally(() => inner.InvalidateCacheDependencies(cacheKey));

            // Inside a SaveChanges or transaction the interceptor announces the tags when it commits.
            if (!pending.TryAdd(cacheKey.CacheDependencies))
            {
                _ = PublishAsync([.. cacheKey.CacheDependencies]);
            }
        }

        public void ClearAllCachedEntries()
        {
            InvalidateLocally(inner.ClearAllCachedEntries);

            _ = PublishAsync([EfCacheInvalidationMessage.AllTag]);
        }

        /// <summary>Called when the transaction that wrote these tags committed: drops what readers cached meanwhile, then tells the other processes.</summary>
        public void ApplyCommitted(string[] tags)
        {
            InvalidateLocally(() => inner.InvalidateCacheDependencies(new EFCacheKey(new SortedSet<string>(tags, StringComparer.OrdinalIgnoreCase))));

            _ = PublishAsync(tags);
        }

        private void InvalidateLocally(Action invalidateLocally)
        {
            try
            {
                invalidateLocally();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Local EF cache invalidation failed; entries expire by TTL.");
            }
        }

        // Not awaited by callers: the interceptor is synchronous and Redis must not slow down SaveChanges.
        private async Task PublishAsync(string[] tags)
        {
            try
            {
                var message = JsonSerializer.Serialize(new EfCacheInvalidationMessage(instanceId.Value, tags));
                await transport.PublishAsync(message, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Publishing the EF cache invalidation failed; other replicas expire by TTL.");
            }
        }
    }
}
