using AMAIS.DXP.Common.Client;
using AMAIS.DXP.Common.Interfaces.Providers;
using AMAIS.DXP.INGRESS.Client.Lib.Api;
using AMAIS.DXP.MD.Client.Lib.Api;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Infrastructure.Behaviours;
using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Infrastructure.Providers.Clients;
using AMAIS.WKFLO.Common.Infrastructure.Providers.Models;
using AMAIS.WKFLO.Common.Interfaces;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Settings;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMediatorBehaviors(this IServiceCollection services)
        {
            services.AddSingleton(typeof(global::Mediator.IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
            services.AddScoped(typeof(global::Mediator.IPipelineBehavior<,>), typeof(ValidatorBehavior<,>));
            services.AddMediatorWkfloMediator();
            return services;
        }

        public static IServiceCollection AddDatastreamProvider(this IServiceCollection services, IConfiguration configuration)
        {
            var elasticsearchSettings = new ElasticsearchSettings();
            configuration.GetSection("ElasticsearchSettings").Bind(elasticsearchSettings);

            var settings = new ElasticsearchClientSettings(new Uri(elasticsearchSettings.Url))
            .Authentication(new ApiKey(elasticsearchSettings.ApiKey));

            services.AddScoped(s => new ElasticsearchClient(settings));
            services.AddScoped<IDatastreamProvider, ElasticsearchDatastreamProvider>();
            return services;
        }

        public static IServiceCollection AddAzureSpeechProvider(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IAzureSpeechProvider, AzureSpeechProvider>();

            return services;
        }

        public static IServiceCollection AddAmaisDatastreamProvider(this IServiceCollection services, IConfiguration configuration)
        {
            var amaisElasticsearchSettings = new EisElasticsearchSettings();
            configuration.GetSection(nameof(EisElasticsearchSettings)).Bind(amaisElasticsearchSettings);

            var settings = new ElasticsearchClientSettings(new Uri(amaisElasticsearchSettings.Url))
            .Authentication(new ApiKey(amaisElasticsearchSettings.ApiKey));

            services.AddScoped(s => new EisElasticsearchClient(settings));
            services.AddScoped<IEisKnowledgeProvider, EisKnowledgeProvider>();
            return services;
        }

        public static IServiceCollection AddBlobStorageConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<BlobStorageSettings>(configuration.GetSection(nameof(BlobStorageSettings)));
            services.AddScoped<IBlobStorageProvider, BlobStorageProvider>();
            services.AddScoped<IImageStorageService, ImageStorageService>();

            return services;
        }

        public static IServiceCollection AddNpgsqlConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<NpgsqlSettings>(configuration.GetSection(nameof(NpgsqlSettings)));
            services.Configure<NpgsqlSettings>(settings =>
            {
                var host = Environment.GetEnvironmentVariable("PG_DB_HOST");
                if (string.IsNullOrEmpty(host))
                    return;

                var port = Environment.GetEnvironmentVariable("PG_DB_PORT") ?? "5432";
                var database = Environment.GetEnvironmentVariable("PG_DB_NAME");
                var user = Environment.GetEnvironmentVariable("PG_DB_USER");

                settings.ConnectionString = $"Host={host};Port={port};Database={database};Username={user};SSL Mode=Require;";
            });
            services.AddSingleton<INpgsqlProvider, NpgsqlProvider>();

            return services;
        }

        public static IServiceCollection AddVectorStoreConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            var embeddingSettings = configuration.GetSection(nameof(EmbeddingSettings)).Get<EmbeddingSettings>();

#pragma warning disable SKEXP0010
            services.AddAzureOpenAIEmbeddingGenerator(
                embeddingSettings.DeploymentName,
                endpoint: embeddingSettings.Endpoint,
                credentials: AzureCredentialHelper.Create(),
                dimensions: 1536
            );
#pragma warning restore SKEXP0010
            services.AddScoped<IVectorStorageProvider, VectorStoragePgsqlProvider<VectorData>>();

            // Bug 8189 / Task 8219: large chat attachments, stored as chunks in one shared collection of the same Postgres database
            services.Configure<ChatDocumentSettings>(configuration.GetSection(nameof(ChatDocumentSettings)));
            services.AddSingleton<IChatDocumentStore, PostgresChatDocumentStore>();

            return services;
        }

        /// <summary>
        /// Registers the DXP Ingress/MD API clients (<see cref="IInteractionApi"/>, <see cref="IContactApi"/>)
        /// shared by every host that needs to send DXP interaction events — the AI API (contact form, chatbot
        /// analytics) and the Worker (batch topic classification analytics). Moved here from the AI API's own
        /// WebAPI ServiceCollectionExtensions so the Worker can reuse it without referencing the AI API's
        /// WebAPI-layer project.
        /// </summary>
        public static IServiceCollection AddAmaisDxpServices(this IServiceCollection services, IConfiguration configuration)
        {
            var oauthBehaviourSettings = new AMAIS.DXP.Common.Client.Models.OAuthBehaviourSettings
            {
                CacheEnable = true,
                RetrieveTokenBeforeExpiration = 60,
                SlidingExpirationTime = 2419200
            };
            configuration.GetSection("OAuthBehaviourSettings").Bind(oauthBehaviourSettings);

            var oauthIngressSettings = new AMAIS.DXP.Common.Client.Models.ClientOAuthSettings();
            configuration.GetSection("IngressClientOAuthSettings").Bind(oauthIngressSettings);

            services.AddSingleton<IClientApiCacheProvider>(provider =>
            {
                var memoryCache = provider.GetRequiredService<IMemoryCache>();
                var logger = provider.GetRequiredService<ILogger<ClientApiCacheProvider>>();
                return new ClientApiCacheProvider(oauthBehaviourSettings, memoryCache, logger);
            });

            var serverUrl = Environment.GetEnvironmentVariable("AMAIS_DXP_INGRESS_SERVER_URL") ?? string.Empty;
            services.AddScoped<IInteractionApi>(provider =>
            {
                if (string.IsNullOrEmpty(serverUrl)) return null!;
                var cacheProvider = provider.GetRequiredService<IClientApiCacheProvider>();
                return new InteractionApi(
                    new HttpClient { BaseAddress = new Uri(serverUrl) },
                    cacheProvider,
                    oauthBehaviourSettings,
                    oauthIngressSettings);
            });

            var oauthMasterDataSettings = new AMAIS.DXP.Common.Client.Models.ClientOAuthSettings();
            configuration.GetSection("MasterDataClientOAuthSettings").Bind(oauthMasterDataSettings);

            var masterDataServerUrl = Environment.GetEnvironmentVariable("AMAIS_DXP_MASTERDATA_SERVER_URL") ?? string.Empty;
            services.AddScoped<IContactApi>(provider =>
            {
                if (string.IsNullOrEmpty(masterDataServerUrl)) return null!;
                var cacheProvider = provider.GetRequiredService<IClientApiCacheProvider>();
                return new ContactApi(
                    masterDataServerUrl,
                    cacheProvider,
                    oauthBehaviourSettings,
                    oauthMasterDataSettings,
                    TimeZoneInfo.Utc.Id);
            });

            return services;
        }

        /// <summary>
        /// Registers the persistence for OAuth 2.1 tokens obtained by OAuth2-authenticated MCP tools
        /// (<c>McpServerConfig.AuthType = OAuth2</c>). Shared by the MD API's interactive OAuth consent
        /// endpoints and the AI API's silent token-refresh path at chat-tool-load time — both must register
        /// this so tokens written by one are readable by the other.
        /// </summary>
        public static IServiceCollection AddMcpOAuthTokenStore(this IServiceCollection services)
        {
            services.AddSingleton<IMcpTokenProtector, AesMcpTokenProtector>();
            services.AddScoped<IMcpOAuthTokenStore, EfMcpOAuthTokenStore>();

            return services;
        }

        /// <summary>
        /// Registers the interactive MCP OAuth 2.1 consent flow (<c>tool/{id}/mcp/oauth/start</c> and the MD
        /// API's single static callback route). Shared by MD API (tenant admin dashboard) and AI API (public
        /// chat widget) — both may call <c>BeginAsync</c>; only MD API's static callback route ever calls
        /// <c>CompleteAsync</c> (see <see cref="IMcpOAuthConsentService"/>'s remarks). Also registers
        /// <see cref="AddMcpOAuthTokenStore"/>, since the consent flow always needs the token store it writes
        /// through.
        /// </summary>
        public static IServiceCollection AddMcpOAuthConsentService(this IServiceCollection services)
        {
            services.AddMcpOAuthTokenStore();
            // Typed HttpClient (IHttpClientFactory-backed) for the hand-rolled OAuth discovery/DCR/token
            // exchange HTTP calls — see McpOAuthConsentService's remarks for why this no longer goes through
            // the ModelContextProtocol SDK's own interactive OAuth machinery.
            services.AddHttpClient<IMcpOAuthConsentService, McpOAuthConsentService>();

            return services;
        }
    }
}
