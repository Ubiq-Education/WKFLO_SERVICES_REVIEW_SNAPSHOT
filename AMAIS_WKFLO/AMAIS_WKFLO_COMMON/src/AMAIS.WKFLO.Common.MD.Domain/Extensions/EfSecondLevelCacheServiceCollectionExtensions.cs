using AMAIS.WKFLO.Common.MD.Domain.Caching;
using AMAIS.WKFLO.Common.Redis;
using EFCoreSecondLevelCacheInterceptor;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace AMAIS.WKFLO.Common.MD.Domain.Extensions
{
    public static class EfSecondLevelCacheServiceCollectionExtensions
    {
        // Prefix of the EF cache keys and tags; the Redis InstanceName already prefixes the L2 keys with amais-wkflo-.
        private const string EfCacheKeyPrefix = "ef-";

        private const int MaximumPayloadBytes = 1024 * 1024;

        /// <summary>
        /// EF Core second-level cache on HybridCache. With Redis configured (see <see cref="RedisSettings"/>) the L2 is shared
        /// by all replicas and invalidations are broadcast; without it the cache is per process (L1 only).
        /// An IDistributedCache registered beforehand (tests) is used as the L2. Note HybridCache ignores a plain MemoryDistributedCache.
        /// </summary>
        public static IServiceCollection AddWkfloEfSecondLevelCache(this IServiceCollection services, bool enableEfCacheLogging = false)
        {
            services.AddHybridCache(options =>
                {
                    options.MaximumPayloadBytes = MaximumPayloadBytes;

                    // Only for entries without their own timeout; EF entries use the .Cacheable() policy timeout.
                    options.DefaultEntryOptions = new HybridCacheEntryOptions
                    {
                        Expiration = TimeSpan.FromMinutes(60),
                        LocalCacheExpiration = TimeSpan.FromSeconds(30)
                    };
                })
                .AddSerializerFactory<NewtonsoftJsonHybridCacheSerializerFactory>();

            services.AddEFSecondLevelCache(options => options
                .UseHybridCacheProvider(CacheExpirationMode.Absolute, TimeSpan.FromMinutes(30))
                .UseCacheKeyPrefix(EfCacheKeyPrefix)
                // A Redis outage falls back to the database instead of failing the query.
                .UseDbCallsIfCachingProviderIsDown(TimeSpan.FromMinutes(1))
                .ConfigureLogging(enableEfCacheLogging));

            // The decorator is always registered (it tags the L1 entries); only with Redis is there anyone to tell.
            if (services.AddWkfloRedis())
            {
                services.AddEfCacheInvalidationBroadcast<RedisEfCacheInvalidationTransport>();
            }
            else
            {
                services.AddEfCacheInvalidationBroadcast<NoOpEfCacheInvalidationTransport>();
            }

            return services;
        }

        /// <summary>
        /// Announces EF cache invalidations through <typeparamref name="TTransport"/> and applies those of other processes.
        /// Must be called after <c>AddEFSecondLevelCache</c>.
        /// </summary>
        public static IServiceCollection AddEfCacheInvalidationBroadcast<TTransport>(this IServiceCollection services)
            where TTransport : class, IEfCacheInvalidationTransport
        {
            var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IEFCacheServiceProvider))
                ?? throw new InvalidOperationException("Register the EF second-level cache before its invalidation broadcast.");

            services.Remove(descriptor);

            services.AddSingleton<IEfCacheInvalidationTransport, TTransport>();
            services.AddSingleton<EfCacheInstanceId>();
            services.AddSingleton<PendingEfCacheInvalidations>();
            services.AddSingleton<EfCacheInvalidationInterceptor>();
            services.AddSingleton(sp =>
            {
                var inner = descriptor.ImplementationType is { } type
                    ? (IEFCacheServiceProvider)ActivatorUtilities.CreateInstance(sp, type)
                    : descriptor.ImplementationFactory!(sp) as IEFCacheServiceProvider
                        ?? throw new InvalidOperationException("Unsupported IEFCacheServiceProvider registration.");

                return ActivatorUtilities.CreateInstance<BroadcastingEfCacheServiceProvider>(sp, inner);
            });
            services.AddSingleton<IEFCacheServiceProvider>(sp => sp.GetRequiredService<BroadcastingEfCacheServiceProvider>());
            services.AddHostedService<EfCacheInvalidationSubscriber>();

            return services;
        }
    }
}
