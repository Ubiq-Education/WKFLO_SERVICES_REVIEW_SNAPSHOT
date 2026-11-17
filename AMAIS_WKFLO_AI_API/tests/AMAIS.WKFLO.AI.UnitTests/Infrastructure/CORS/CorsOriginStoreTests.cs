using AMAIS.WKFLO.AI.WebAPI.Infrastructure.CORS;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.CORS
{
    internal sealed class CorsTestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    internal sealed class CorsTestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public System.Collections.Concurrent.ConcurrentQueue<Exception> Errors { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (exception != null) Errors.Enqueue(exception);
        }
    }

    internal sealed class CorsTestLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    /// <summary>
    /// Shared inline fixture: a fake DB whose WebOrigins reads are counted, can fail and can be held back by a gate.
    /// </summary>
    internal sealed class CorsTestEnvironment
    {
        public CorsTestTimeProvider Time { get; } = new();
        public List<WebOrigin> Rows { get; } = new();
        public Dictionary<string, string> Config { get; } = new();
        public int Loads;
        public Exception Failure;
        public TaskCompletionSource Gate;
        public TaskCompletionSource LoadStarted;
        public CorsTestLogger<CorsOriginStore> Logger { get; } = new();

        private readonly Mock<IMasterDataDbContext> _dbMock = new();

        public CorsTestEnvironment()
        {
            _dbMock.Setup(x => x.WebOrigins).Returns(() =>
            {
                Interlocked.Increment(ref Loads);
                LoadStarted?.TrySetResult();
                if (Failure != null) throw Failure;
                Gate?.Task.GetAwaiter().GetResult();
                return Rows.ToArray().BuildMockDbSet().Object;
            });
        }

        public IConfiguration BuildConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(Config).Build();

        public CorsOriginStore CreateStore()
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => _dbMock.Object);
            return new CorsOriginStore(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                BuildConfiguration(), Time, new CorsTestLifetime(), Logger);
        }

        public static WebOrigin Row(string name, bool active = true, bool deleted = false, string type = "SignalR")
            => new() { Name = name, IsActive = active, IsDeleted = deleted, WebOriginType = type };
    }

    public class CorsOriginStoreTests
    {
        private readonly CorsTestEnvironment _env = new();
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(10, Ct);

            Assert.True(condition(), "Condition not met in time: " + string.Join(" | ", _env.Logger.Errors));
        }

        [Fact]
        public async Task FirstCall_LoadsFromDb_AndSecondCallWithinTtl_DoesNot()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var store = _env.CreateStore();

            await store.EnsureFreshAsync("https://a.com", Ct);
            await store.EnsureFreshAsync("https://a.com", Ct);

            Assert.Equal(1, _env.Loads);
            Assert.True(store.IsOriginAllowed("https://a.com"));
        }

        [Fact]
        public async Task AfterTtl_KnownOrigin_ReloadsInBackground()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync("https://a.com", Ct);

            _env.Rows.Add(CorsTestEnvironment.Row("https://b.com"));
            _env.Time.Advance(TimeSpan.FromMinutes(5));
            await store.EnsureFreshAsync("https://a.com", Ct);

            await WaitUntilAsync(() => store.IsOriginAllowed("https://b.com"));
            Assert.Equal(2, _env.Loads);
        }

        [Fact]
        public async Task AfterTtl_KnownOrigin_DoesNotBlockOnTheDatabase()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync("https://a.com", Ct);

            _env.Gate = new TaskCompletionSource();
            _env.LoadStarted = new TaskCompletionSource();
            _env.Rows.Add(CorsTestEnvironment.Row("https://b.com"));
            _env.Time.Advance(TimeSpan.FromMinutes(6));

            var call = store.EnsureFreshAsync("https://a.com", Ct);
            await _env.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

            Assert.True(call.IsCompletedSuccessfully);
            Assert.True(store.IsOriginAllowed("https://a.com"));
            Assert.False(store.IsOriginAllowed("https://b.com"));

            _env.Gate.SetResult();
            await WaitUntilAsync(() => store.IsOriginAllowed("https://b.com"));
            Assert.Equal(2, _env.Loads);
        }

        [Fact]
        public async Task AfterTtl_ManyKnownOriginCalls_StartOneBackgroundLoad()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync("https://a.com", Ct);

            _env.Gate = new TaskCompletionSource();
            _env.LoadStarted = new TaskCompletionSource();
            _env.Time.Advance(TimeSpan.FromMinutes(6));
            for (var i = 0; i < 10; i++)
                await store.EnsureFreshAsync("https://a.com", Ct);
            await _env.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            _env.Gate.SetResult();
            await WaitUntilAsync(() => Volatile.Read(ref _env.Loads) >= 2);
            await Task.Delay(50, Ct);

            Assert.Equal(2, _env.Loads);
        }

        [Fact]
        public async Task UnknownOrigin_TriggersReload_AndNewlyAddedDomainIsAllowed()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync("https://a.com", Ct);

            _env.Rows.Add(CorsTestEnvironment.Row("https://new.com"));
            _env.Time.Advance(TimeSpan.FromSeconds(31));
            await store.EnsureFreshAsync("https://new.com", Ct);

            Assert.Equal(2, _env.Loads);
            Assert.True(store.IsOriginAllowed("https://new.com"));
        }

        [Fact]
        public async Task UnknownOrigin_WithinThrottle_DoesNotReloadAgain()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync("https://x.com", Ct);

            _env.Time.Advance(TimeSpan.FromSeconds(10));
            await store.EnsureFreshAsync("https://y.com", Ct);
            await store.EnsureFreshAsync("https://z.com", Ct);

            Assert.Equal(1, _env.Loads);
            Assert.False(store.IsOriginAllowed("https://y.com"));
        }

        [Fact]
        public async Task Matching_IgnoresCaseAndTrailingSlash_AndFiltersRows()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://Site.com/"));
            _env.Rows.Add(CorsTestEnvironment.Row("https://inactive.com", active: false));
            _env.Rows.Add(CorsTestEnvironment.Row("https://deleted.com", deleted: true));
            _env.Rows.Add(CorsTestEnvironment.Row("https://other-type.com", type: "Widget"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync(null, Ct);

            Assert.True(store.IsOriginAllowed("https://site.COM"));
            Assert.True(store.IsOriginAllowed("https://site.com/"));
            Assert.False(store.IsOriginAllowed("https://inactive.com"));
            Assert.False(store.IsOriginAllowed("https://deleted.com"));
            Assert.False(store.IsOriginAllowed("https://other-type.com"));
        }

        [Fact]
        public async Task Matching_IsIdnSafe_InBothDirections()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://bücher.example"));
            _env.Rows.Add(CorsTestEnvironment.Row("https://xn--mnchen-3ya.example"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync(null, Ct);

            Assert.True(store.IsOriginAllowed("https://xn--bcher-kva.example"));
            Assert.True(store.IsOriginAllowed("https://bücher.example"));
            Assert.True(store.IsOriginAllowed("https://münchen.example"));
            Assert.False(store.IsOriginAllowed("https://other.example"));
        }

        [Fact]
        public async Task DbFailure_KeepsLastSnapshot()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync("https://a.com", Ct);

            _env.Failure = new InvalidOperationException("db down");
            _env.Time.Advance(TimeSpan.FromSeconds(31));
            await store.EnsureFreshAsync("https://unknown.com", Ct);

            Assert.Equal(2, _env.Loads);
            Assert.True(store.IsOriginAllowed("https://a.com"));
        }

        [Fact]
        public async Task BackgroundDbFailure_IsObservedAndKeepsLastSnapshot()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var store = _env.CreateStore();
            await store.EnsureFreshAsync("https://a.com", Ct);

            _env.Failure = new InvalidOperationException("db down");
            _env.Time.Advance(TimeSpan.FromMinutes(6));
            await store.EnsureFreshAsync("https://a.com", Ct);
            await WaitUntilAsync(() => Volatile.Read(ref _env.Loads) >= 2);

            Assert.True(store.IsOriginAllowed("https://a.com"));
        }

        [Fact]
        public async Task EmptySnapshot_AllowsLocalhostOnly()
        {
            var store = _env.CreateStore();
            await store.EnsureFreshAsync(null, Ct);

            Assert.True(store.IsOriginAllowed("http://localhost"));
            Assert.False(store.IsOriginAllowed("https://a.com"));
        }

        [Fact]
        public async Task ConcurrentCalls_LoadOnce()
        {
            const int callers = 20;
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            _env.Gate = new TaskCompletionSource();
            _env.LoadStarted = new TaskCompletionSource();
            var store = _env.CreateStore();

            var started = 0;
            var tasks = new List<Task>();
            for (var i = 0; i < callers; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    Interlocked.Increment(ref started);
                    return store.EnsureFreshAsync("https://a.com", Ct);
                }, Ct));
            }

            // The first caller is inside the load and every other caller has started, so they overlap with it.
            await _env.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            await WaitUntilAsync(() => Volatile.Read(ref started) == callers);
            _env.Gate.SetResult();
            await Task.WhenAll(tasks);

            Assert.Equal(1, _env.Loads);
        }

        [Fact]
        public async Task CallerCancelledDuringReload_StopsWaiting_AndDoesNotBreakOtherWaiters()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            _env.Gate = new TaskCompletionSource();
            _env.LoadStarted = new TaskCompletionSource();
            var store = _env.CreateStore();
            using var firstCaller = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            using var waiterCaller = CancellationTokenSource.CreateLinkedTokenSource(Ct);

            // Called directly, not through Task.Run: the load runs on its own thread and is held by the gate,
            // so each call has joined the in-flight load when it returns. With Task.Run a busy thread pool
            // could start a waiter only after the load finished, and then it has nothing to wait for.
            var first = store.EnsureFreshAsync("https://a.com", firstCaller.Token);
            await _env.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            var cancelledWaiter = store.EnsureFreshAsync("https://a.com", waiterCaller.Token);
            var otherWaiter = store.EnsureFreshAsync("https://a.com", Ct);
            Assert.False(first.IsCompleted);
            Assert.False(cancelledWaiter.IsCompleted);
            Assert.False(otherWaiter.IsCompleted);

            // The request that started the load is aborted while the load runs, and a waiter gives up.
            firstCaller.Cancel();
            waiterCaller.Cancel();
            _env.Gate.SetResult();

            // Both cancelled callers stop waiting, including the one that started the load.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);
            await otherWaiter;
            Assert.Equal(1, _env.Loads);
            Assert.True(store.IsOriginAllowed("https://a.com"));
        }
    }
}
