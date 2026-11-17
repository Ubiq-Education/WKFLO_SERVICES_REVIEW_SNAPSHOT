using AMAIS.WKFLO.AI.Application.Extensions;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.HealthCheck;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.CORS;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using AMAIS.WKFLO.Common.MD.Domain.Caching;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Extensions;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Settings;
using EFCoreSecondLevelCacheInterceptor;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace AMAIS.WKFLO.AI.FunctionalTests
{
    public class Setup(IConfiguration configuration)
    {
        public IConfiguration Configuration => configuration;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Register CORS Dynamic Policy Provider
            services.AddDynamicCors();

            // Add services to the container.
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(Configuration.GetSection("AzureAd"));

            services.AddControllers(options =>
            {
                options.Filters.Add(typeof(HttpGlobalExceptionFilter));
                options.ModelValidatorProviders.Clear();
            })
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.TypeNameHandling = TypeNameHandling.None;
            })
            .AddApplicationPart(typeof(HealthCheckOK).Assembly);

            services.AddDomainTest();
            services.AddApplication(Configuration);
            services.AddInfrastructure(Configuration);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseCors(Configuration.GetSection(nameof(CorsSettings))["PolicyName"]);

            app.UseRouting();

            app.UseMiddleware<TenantContextResolver>();
            app.UseMiddleware<ChatContextResolver>();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseMiddleware<UserContextResolver>();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }

    internal static class ServiceCollectionExtensions
    {
        private const string InMemoryConnectionString = "DataSource=:memory:";

        private static readonly ILoggerFactory consoleFactory = LoggerFactory.Create(builder => builder.AddConsole());

        public static IServiceCollection AddDomainTest(this IServiceCollection services)
        {
            // In-memory L2 stands in for Redis (wrapped, because HybridCache ignores a plain MemoryDistributedCache).
            services.AddSingleton<IDistributedCache, WrappedDistributedCache>();
            services.AddWkfloEfSecondLevelCache(enableEfCacheLogging: true);

            var _connection = new SqliteConnection(InMemoryConnectionString);
            _connection.Open();

            //Configure DB for Entity Framework
            services.AddDbContextPool<MasterDataDbContext>((serviceProvider, optionsBuilder) =>
            {
                optionsBuilder.UseSqlite(_connection)
                //Add the DB Cache Interceptor, it will automaticly watch the CRUD operations and update the cache accordingly
                .AddInterceptors(
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

    internal static class TestConstants
    {
        public static string Audience { get; set; } = "Audience";

        public static string Issuer { get; set; } = "Issuer";
    }

    internal class TokenBuilder
    {
        public static string BuildJWTToken()
        {
            var settings = new Settings
            {
                SymmetricFuncTestKey = "key for functional tests key for functional tests"
            };
            var privateKey = settings.SymmetricFuncTestKey;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(privateKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var jwtValidity = DateTime.UtcNow.AddMinutes(5);

            var token = new JwtSecurityToken(
                TestConstants.Issuer,
                TestConstants.Audience,
                expires: jwtValidity,
                signingCredentials: creds,
                claims:
                [
                    new ("scp", "access_as_user")
                ]);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    internal class Settings
    {
        public string SymmetricFuncTestKey { get; set; }
    }
}
