using AMAIS.WKFLO.Common.ApiClient.Client.Models;
using AMAIS.WKFLO.Common.ApiClient.Providers.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.ApiClient.Providers
{
    public class ClientApiCacheProvider : IClientApiCacheProvider
    {
        private readonly IMemoryCache _memoryCache;
        private readonly OAuthBehaviourSettings _oauthBehaviourSettings;
        private readonly ILogger<ClientApiCacheProvider> _logger;

        public ClientApiCacheProvider(OAuthBehaviourSettings oauthBehaviourSettings, IMemoryCache memoryCache, ILogger<ClientApiCacheProvider> logger)
        {
            _oauthBehaviourSettings = oauthBehaviourSettings;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public void SetCacheEntry(string key, string value, long expireTime = 0)
        {
            try
            {
                InMemorySet(key, value, expireTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while Set: {key} {value}", key, value);
            }

        }

        private void InMemorySet(string key, string value, long expireTime)
        {
            // Set cache options.
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(expireTime - _oauthBehaviourSettings.RetrieveTokenBeforeExpiration))
                .SetSlidingExpiration(TimeSpan.FromSeconds(_oauthBehaviourSettings.SlidingExpirationTime));

            // Save data in cache.
            _memoryCache.Set(key, value, cacheEntryOptions);
        }

        public string TryGetEntryValue(string key)
        {
            try
            {
                _memoryCache.TryGetValue(key, out string result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while TryGetValue: {key}", key);
                return null;
            }
        }
    }
}
