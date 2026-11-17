using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.Common.Redis
{
    public static class RedisServiceCollectionExtensions
    {
        /// <summary>
        /// Prefix of every key this platform writes to the shared Redis, distinct from the DXP keys (amais-dxp-*).
        /// Staging and prod use separate Redis instances, so no per-environment suffix is needed.
        /// </summary>
        public const string InstanceName = "amais-wkflo-";

        /// <summary>
        /// Registers the shared Redis connection and <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> (the HybridCache L2)
        /// when Redis is configured through environment variables. Returns false, registering nothing, when it is not:
        /// HybridCache then runs L1-only (local runs, Worker, tests).
        /// </summary>
        public static bool AddWkfloRedis(this IServiceCollection services) => services.AddWkfloRedis(RedisSettings.FromEnvironment());

        public static bool AddWkfloRedis(this IServiceCollection services, RedisSettings settings)
        {
            if (!settings.IsConfigured)
            {
                return false;
            }

            services.AddSingleton(_ => new RedisConnectionProvider(settings));

            services.AddStackExchangeRedisCache(_ => { });
            services.AddOptions<RedisCacheOptions>()
                .Configure<RedisConnectionProvider>((options, provider) =>
                {
                    options.InstanceName = InstanceName;
                    options.ConnectionMultiplexerFactory = provider.GetConnectionAsync;
                });

            // A Redis outage must not slow every request down: the breaker makes the L2 behave as empty while Redis is failing.
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(sp => new CircuitBreakerDistributedCache(
                new RedisCache(sp.GetRequiredService<IOptions<RedisCacheOptions>>()),
                sp.GetRequiredService<ILogger<CircuitBreakerDistributedCache>>()));

            return true;
        }
    }
}
