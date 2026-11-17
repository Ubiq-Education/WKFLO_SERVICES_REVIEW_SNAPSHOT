#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.AI.FunctionalTests
{
    /// <summary>
    /// In-memory stand-in for Redis. HybridCache disables its L2 when the distributed cache is exactly a MemoryDistributedCache,
    /// so the memory cache has to be wrapped for the L2 to take part.
    /// </summary>
    public sealed class WrappedDistributedCache : IDistributedCache
    {
        private readonly IDistributedCache _inner = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        public byte[]? Get(string key) => _inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            _inner.SetAsync(key, value, options, token);

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public void Remove(string key) => _inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
    }
}
