using AMAIS.WKFLO.Common.Redis;
using StackExchange.Redis;

namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    public sealed class RedisEfCacheInvalidationTransport(RedisConnectionProvider connectionProvider) : IEfCacheInvalidationTransport
    {
        private static readonly RedisChannel Channel = RedisChannel.Literal($"{RedisServiceCollectionExtensions.InstanceName}ef-cache-invalidation");

        public async Task PublishAsync(string message, CancellationToken cancellationToken)
        {
            var connection = await connectionProvider.GetConnectionAsync().WaitAsync(cancellationToken);

            // Awaited, so a failed or disconnected publish reaches the caller, which logs it (the caller itself is not awaited by SaveChanges).
            await connection.GetSubscriber().PublishAsync(Channel, message);
        }

        public async Task SubscribeAsync(Func<string, Task> handler, Func<Task> onReconnected, CancellationToken cancellationToken)
        {
            var connection = await connectionProvider.GetConnectionAsync().WaitAsync(cancellationToken);
            var subscriber = connection.GetSubscriber();

            Action<RedisChannel, RedisValue> onMessage = (channel, value) =>
            {
                if (value.HasValue)
                {
                    _ = handler(value.ToString());
                }
            };

            try
            {
                await subscriber.SubscribeAsync(Channel, onMessage).WaitAsync(cancellationToken);
            }
            catch
            {
                // The handler is registered before the server round trip; drop it so a retry cannot duplicate it.
                subscriber.Unsubscribe(Channel, onMessage, CommandFlags.FireAndForget);
                throw;
            }

            // StackExchange.Redis restores the subscription itself; messages sent while it was down are lost.
            connection.ConnectionRestored += (_, args) =>
            {
                if (args.ConnectionType == ConnectionType.Subscription)
                {
                    _ = onReconnected();
                }
            };
        }
    }
}
