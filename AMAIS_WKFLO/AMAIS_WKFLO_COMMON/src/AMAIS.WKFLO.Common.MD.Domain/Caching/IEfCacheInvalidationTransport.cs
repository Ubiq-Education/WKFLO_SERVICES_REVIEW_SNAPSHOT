namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>
    /// Carries EF cache invalidation messages between the replicas of all services sharing the cache.
    /// Redis pub/sub in deployed environments, in-memory in tests.
    /// </summary>
    public interface IEfCacheInvalidationTransport
    {
        Task PublishAsync(string message, CancellationToken cancellationToken);

        /// <summary>
        /// Registers the handler that receives every message published on the channel, including those of this process.
        /// <paramref name="onReconnected"/> runs when the subscription connection was re-established: messages sent meanwhile are lost.
        /// </summary>
        Task SubscribeAsync(Func<string, Task> handler, Func<Task> onReconnected, CancellationToken cancellationToken);
    }
}
