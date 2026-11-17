using AMAIS.DXP.Common.Client;
using AMAIS.DXP.Common.Client.Models;
using AMAIS.DXP.Common.Interfaces.Providers;
using AMAIS.DXP.REPORT.Client.Lib.Api;
using AMAIS.WKFLO.DXP.MCP.Application.Pages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.DXP.MCP.Application.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public const string ReportServerUrlVariable = "AMAIS_DXP_REPORT_SERVER_URL";

        public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDxpReportApi(configuration);
            services.AddScoped<IDxpPageStatsSource, DocumentByPageStatsSource>();

            return services;
        }

        /// <summary>
        /// Registers the DXP Report API client the same way PetraCMS does (<c>AddAmaisDxpServices</c>):
        /// <c>ReportApi(AMAIS_DXP_REPORT_SERVER_URL, IClientApiCacheProvider, OAuthBehaviourSettings, ReportClientOAuthSettings)</c>.
        /// Outbound auth is chosen by <c>AMAIS_DXP_CLIENT_AUTH_FLOW</c> inside the client (e.g. <c>entraid-no-secrets</c>
        /// with the WKFLO managed identity in deployed environments).
        /// </summary>
        public static IServiceCollection AddDxpReportApi(this IServiceCollection services, IConfiguration configuration)
        {
            // Same defaults as the other WKFLO DXP clients (Common.Infrastructure AddAmaisDxpServices); config overrides them.
            var oauthBehaviourSettings = new OAuthBehaviourSettings
            {
                CacheEnable = true,
                RetrieveTokenBeforeExpiration = 60,
                SlidingExpirationTime = 2419200
            };
            configuration.GetSection("OAuthBehaviourSettings").Bind(oauthBehaviourSettings);

            var oauthReportSettings = new ClientOAuthSettings();
            configuration.GetSection("ReportClientOAuthSettings").Bind(oauthReportSettings);

            var reportServerUrl = Environment.GetEnvironmentVariable(ReportServerUrlVariable);

            services.AddMemoryCache();
            services.AddSingleton<IClientApiCacheProvider>(provider =>
            {
                var memoryCache = provider.GetRequiredService<IMemoryCache>();
                var logger = provider.GetRequiredService<ILogger<ClientApiCacheProvider>>();
                return new ClientApiCacheProvider(oauthBehaviourSettings, memoryCache, logger);
            });

            services.AddScoped<IReportApi>(provider =>
            {
                if (string.IsNullOrWhiteSpace(reportServerUrl))
                {
                    throw new InvalidOperationException($"{ReportServerUrlVariable} is not configured.");
                }

                return new ReportApi(reportServerUrl, provider.GetRequiredService<IClientApiCacheProvider>(), oauthBehaviourSettings, oauthReportSettings);
            });

            return services;
        }
    }
}
