using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Actors;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Settings;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using AMAIS.WKFLO.MD.WebAPI.Models.Settings;
using Dapr.Actors.Client;
using Dapr.Client;
using Polly;
using Polly.Retry;

namespace AMAIS.WKFLO.MD.WebAPI.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions();
            services.Configure<ElasticsearchSettings>(configuration.GetSection(nameof(ElasticsearchSettings)));
            services.Configure<ChatWidgetSettings>(configuration.GetSection(nameof(ChatWidgetSettings)));
            services.Configure<ClassificationJobSettings>(configuration.GetSection(nameof(ClassificationJobSettings)));
            services.Configure<McpOAuthSettings>(configuration.GetSection(nameof(McpOAuthSettings)));

            services.AddScoped<TenantContext>();
            services.AddScoped<UserContext>();
            services.AddScoped<AgentSharableContext>();
            services.AddScoped<ChatContext>();
            services.AddScoped<IDateTimeProvider, DateTimeProvider>();
            services.AddDatastreamProvider(configuration);
            services.AddBlobStorageConfiguration(configuration);
            services.AddNpgsqlConfiguration(configuration);
            services.AddVectorStoreConfiguration(configuration);

            services.AddSingleton<IActorProxyFactory>(_ => new ActorProxyFactory());
            services.AddDaprClient();
            services.AddActors(options =>
            {
                options.Actors.RegisterActor<ClassificationJobSchedulerActor>();
            });

            return services;
        }

        public static async Task WaitForDaprSidecarWithPollyAsync(CancellationToken cancellationToken = default)
        {
            using var client = new DaprClientBuilder().Build();

            // Define retry policy: exponential backoff, up to 10 retries
            AsyncRetryPolicy retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 10,
                    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(2000),
                    onRetry: (outcome, timespan, attempt, context) =>
                    {
                        Console.WriteLine($"Waiting for Dapr sidecar... attempt {attempt}");
                    });

            // Execute wait-for-sidecar within Polly
            await retryPolicy.ExecuteAsync(async ct =>
            {
                await client.WaitForSidecarAsync(cancellationToken: cancellationToken);
                Console.WriteLine("Dapr sidecar is ready!");
            }, cancellationToken);
        }
    }
}
