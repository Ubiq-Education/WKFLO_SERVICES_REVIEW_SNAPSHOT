namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>Used without Redis: there is no other process to tell.</summary>
    public sealed class NoOpEfCacheInvalidationTransport : IEfCacheInvalidationTransport
    {
        public Task PublishAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubscribeAsync(Func<string, Task> handler, Func<Task> onReconnected, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
