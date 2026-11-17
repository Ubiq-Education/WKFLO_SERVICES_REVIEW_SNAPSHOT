using AMAIS.WKFLO.Common.MD.Domain.Caching;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using EFCoreSecondLevelCacheInterceptor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.MD.Domain.Extensions
{
    public static class ServiceCollectionExtensions
    {
        private static readonly ILoggerFactory consoleFactory = LoggerFactory.Create(builder => builder.AddConsole());

        public static IServiceCollection AddDomain(this IServiceCollection services)
        {
            //Add Db Caching Interceptor for Entity Framework (HybridCache, shared through Redis when configured).
            services.AddWkfloEfSecondLevelCache();

            //Configure DB for Entity Framework
            services.AddDbContextPool<MasterDataDbContext>((serviceProvider, optionsBuilder) =>
            {
                optionsBuilder.UseSqlServer(
                    //You need to set up this on your Enviromental variables/ Azure web abb Application Settings
                    Environment.GetEnvironmentVariable("DB_CONNECTION_STRING").Replace("\\\\", "\\"),
                    sqlServerOptionsBuilder =>
                    {
                        sqlServerOptionsBuilder
                            .UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery)
                            .CommandTimeout((int)TimeSpan.FromMinutes(3).TotalSeconds)
                            .EnableRetryOnFailure();
                    })
                //Add the DB Cache Interceptor, it will automaticly watch the CRUD operations and update the cache accordingly
                .AddInterceptors(
                    // Must come first: it tells the cache which context a write belongs to and publishes invalidations on commit.
                    serviceProvider.GetRequiredService<EfCacheInvalidationInterceptor>(),
                    serviceProvider.GetRequiredService<SecondLevelCacheInterceptor>());

#if DEBUG
                optionsBuilder.UseLoggerFactory(consoleFactory);
#endif
            });

            services.AddScoped<IMasterDataDbContext, MasterDataDbContext>();

            return services;
        }
    }
}
