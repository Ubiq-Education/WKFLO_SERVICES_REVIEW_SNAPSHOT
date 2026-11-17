using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>
    /// One subscription per process: applies the invalidations announced by other processes to the local HybridCache,
    /// which also records the invalidation time so stale L2 payloads are rejected.
    /// </summary>
    public sealed class EfCacheInvalidationSubscriber(
        IEfCacheInvalidationTransport transport,
        HybridCache hybridCache,
        EfCacheInstanceId instanceId,
        ILogger<EfCacheInvalidationSubscriber> logger) : BackgroundService
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Redis being unreachable at startup must not stop the host: retry until subscribed.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await transport.SubscribeAsync(HandleAsync, OnReconnectedAsync, stoppingToken);
                    return;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Subscribing to EF cache invalidations failed; retrying in {Delay}.", RetryDelay);
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
        }

        // Messages published while the subscription connection was down are lost, so nothing cached before can be trusted.
        public async Task OnReconnectedAsync()
        {
            try
            {
                logger.LogWarning("EF cache invalidation subscription was re-established; clearing the local EF cache.");
                await hybridCache.RemoveByTagAsync(EfCacheInvalidationMessage.AllTag);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Clearing the EF cache after reconnecting failed; entries expire by TTL.");
            }
        }

        public async Task HandleAsync(string payload)
        {
            try
            {
                var message = JsonSerializer.Deserialize<EfCacheInvalidationMessage>(payload);

                if (message is null || message.Tags is not { Length: > 0 } || message.SenderId == instanceId.Value)
                {
                    return;
                }

                await hybridCache.RemoveByTagAsync(message.Tags);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Applying an EF cache invalidation failed; entries expire by TTL.");
            }
        }
    }
}
