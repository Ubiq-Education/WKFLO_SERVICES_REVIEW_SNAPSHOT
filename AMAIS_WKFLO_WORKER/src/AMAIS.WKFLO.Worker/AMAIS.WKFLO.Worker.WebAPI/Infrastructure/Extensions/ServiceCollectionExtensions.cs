using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Dapr.Client;
using Polly;
using Polly.Retry;

namespace AMAIS.WKFLO.Worker.WebAPI.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddWorkerInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IDateTimeProvider, DateTimeProvider>();
            services.AddDaprClient();

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
