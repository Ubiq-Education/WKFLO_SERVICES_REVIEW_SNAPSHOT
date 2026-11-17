#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using AMAIS.WKFLO.Common.MD.Domain.Caching;
using AMAIS.WKFLO.Common.Redis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AMAIS.WKFLO.MD.UnitTests.Infrastructure.Caching
{
    public class RedisResilienceTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task CircuitBreaker_SkipsTheCache_AfterAFailure()
        {
            var inner = new Mock<IDistributedCache>();
            inner.Setup(cache => cache.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));
            var breaker = new CircuitBreakerDistributedCache(inner.Object, NullLogger<CircuitBreakerDistributedCache>.Instance);

            Assert.Null(await breaker.GetAsync("a", Ct)); // fails, opens the breaker
            Assert.Null(await breaker.GetAsync("b", Ct));
            await breaker.SetAsync("c", [1], new DistributedCacheEntryOptions(), Ct);
            await breaker.RemoveAsync("d", Ct);

            inner.Verify(cache => cache.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            inner.Verify(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
            inner.Verify(cache => cache.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CircuitBreaker_PassesThroughWhileTheCacheWorks()
        {
            var inner = new Mock<IDistributedCache>();
            inner.Setup(cache => cache.GetAsync("a", It.IsAny<CancellationToken>())).ReturnsAsync([7]);
            var breaker = new CircuitBreakerDistributedCache(inner.Object, NullLogger<CircuitBreakerDistributedCache>.Instance);

            Assert.Equal(new byte[] { 7 }, await breaker.GetAsync("a", Ct));
            Assert.Equal(new byte[] { 7 }, await breaker.GetAsync("a", Ct));
        }

        [Fact]
        public async Task CircuitBreaker_DoesNotOpen_WhenTheCallerCancels()
        {
            var inner = new Mock<IDistributedCache>();
            inner.SetupSequence(cache => cache.GetAsync("a", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException())
                .ReturnsAsync([7]);
            var breaker = new CircuitBreakerDistributedCache(inner.Object, NullLogger<CircuitBreakerDistributedCache>.Instance);

            await Assert.ThrowsAsync<OperationCanceledException>(() => breaker.GetAsync("a", Ct));

            Assert.Equal(new byte[] { 7 }, await breaker.GetAsync("a", Ct));
        }

        [Fact]
        public void ConnectionProvider_CanBeDisposedSynchronously()
        {
            // A ServiceProvider disposed with Dispose() throws for services that only implement IAsyncDisposable.
            var services = new ServiceCollection();
            services.AddSingleton(_ => new RedisConnectionProvider(RedisSettings.From(_ => null)));
            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<RedisConnectionProvider>();

            provider.Dispose();
        }

        [Fact]
        public async Task Subscriber_AppliesAnotherReplicasInvalidation_AndSkipsItsOwn()
        {
            var cache = new Mock<HybridCache> { CallBase = true };
            cache.Setup(c => c.RemoveByTagAsync("t", It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
            var subscriber = new EfCacheInvalidationSubscriber(
                new NoOpEfCacheInvalidationTransport(), cache.Object, new EfCacheInstanceId("me"),
                NullLogger<EfCacheInvalidationSubscriber>.Instance);

            await subscriber.HandleAsync("""{"SenderId":"me","Tags":["t"]}""");
            await subscriber.HandleAsync("""{"SenderId":"other","Tags":["t"]}""");

            cache.Verify(c => c.RemoveByTagAsync("t", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
