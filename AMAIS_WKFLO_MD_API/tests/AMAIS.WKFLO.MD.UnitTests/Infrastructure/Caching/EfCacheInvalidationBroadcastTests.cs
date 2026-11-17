#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AMAIS.WKFLO.Common.MD.Domain.Caching;
using AMAIS.WKFLO.Common.MD.Domain.Extensions;
using EFCoreSecondLevelCacheInterceptor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AMAIS.WKFLO.MD.UnitTests.Infrastructure.Caching
{
    public class EfCacheInvalidationBroadcastTests : IDisposable
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private readonly SqliteConnection _keepAlive;
        // A file in WAL mode, so a reader sees the old row while another connection has an uncommitted write (as SQL Server does).
        private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"efcache-{Guid.NewGuid():N}.db");
        private readonly string _connectionString;
        private readonly IDistributedCache _sharedL2 = new WrappedDistributedCache();
        private readonly InMemoryBus _bus = new();
        private readonly List<ServiceProvider> _providers = [];

        public EfCacheInvalidationBroadcastTests()
        {
            _connectionString = $"DataSource={_databaseFile};Pooling=false";
            _bus.NameAtPublish = ReadNameFromDatabase;
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
            using (var command = _keepAlive.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL";
                command.ExecuteNonQuery();
            }

            using var context = new ThingDbContext(new DbContextOptionsBuilder<ThingDbContext>().UseSqlite(_connectionString).Options);
            context.Database.EnsureCreated();
            context.Things.Add(new Thing
            {
                Id = 1,
                Name = "v1",
                Key = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
                Amount = 12.5m,
                Active = true,
                CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(-3)),
                Payload = [1, 2, 3]
            });
            context.SaveChanges();
        }

        public void Dispose()
        {
            foreach (var provider in _providers)
            {
                provider.Dispose();
            }

            _keepAlive.Dispose();

            foreach (var file in new[] { _databaseFile, _databaseFile + "-wal", _databaseFile + "-shm" })
            {
                File.Delete(file);
            }
        }

        // What another connection sees right now: the new name only after the writer committed.
        private string ReadNameFromDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name FROM Things WHERE Id = 1";

            return (string)command.ExecuteScalar()!;
        }

        [Fact]
        public async Task CachedResult_RoundTripsEveryColumnTypeThroughTheDistributedCache()
        {
            var replicaA = await StartReplicaAsync(broadcast: true);
            var replicaB = await StartReplicaAsync(broadcast: true);

            var fromDatabase = await ReadAsync(replicaA);
            await RenameInDatabaseOnlyAsync("changed behind the cache"); // B can only get v1 from L2
            var fromCache = await ReadAsync(replicaB);

            Assert.Equal(fromDatabase, fromCache);
            Assert.Equal(12.5m, fromCache.Amount);
            Assert.Equal(TimeSpan.FromHours(-3), fromCache.CreatedAt.Offset);
            Assert.Equal(new byte[] { 1, 2, 3 }, fromCache.Payload);
        }

        [Fact]
        public async Task Write_IsVisibleInOtherReplica_WhenInvalidationIsBroadcast()
        {
            var writer = await StartReplicaAsync(broadcast: true);
            var reader = await StartReplicaAsync(broadcast: true);
            Assert.Equal("v1", (await ReadAsync(writer)).Name);
            Assert.Equal("v1", (await ReadAsync(reader)).Name);

            await RenameAsync(writer, "v2");
            await _bus.DeliverAsync();

            Assert.Equal("v2", (await ReadAsync(reader)).Name);
        }

        // Regression: a replica that warmed its L1 from L2 (not from the database) must also drop it on RemoveByTag.
        [Fact]
        public async Task Write_IsVisibleInReplicaWarmedFromL2_WhenInvalidationIsBroadcast()
        {
            var writer = await StartReplicaAsync(broadcast: true);
            var reader = await StartReplicaAsync(broadcast: true);
            Assert.Equal("v1", (await ReadAsync(writer)).Name); // populates L2
            Assert.Equal("v1", (await ReadAsync(reader)).Name); // L1 filled from L2

            await RenameAsync(writer, "v2");
            await _bus.DeliverAsync();

            Assert.Equal("v2", (await ReadAsync(reader)).Name);
        }

        [Fact]
        public async Task Write_LeavesOtherReplicaStale_WithoutTheBroadcast()
        {
            var writer = await StartReplicaAsync(broadcast: false);
            var reader = await StartReplicaAsync(broadcast: false);
            Assert.Equal("v1", (await ReadAsync(writer)).Name);
            Assert.Equal("v1", (await ReadAsync(reader)).Name);

            await RenameAsync(writer, "v2");

            // Documents why the broadcast exists: HybridCache in the reader never re-reads the tag invalidation from L2.
            Assert.Equal("v1", (await ReadAsync(reader)).Name);
        }

        [Fact]
        public async Task Write_IsVisibleInTheWritingReplica()
        {
            var writer = await StartReplicaAsync(broadcast: true);
            Assert.Equal("v1", (await ReadAsync(writer)).Name);

            await RenameAsync(writer, "v2");

            Assert.Equal("v2", (await ReadAsync(writer)).Name);
        }

        [Fact]
        public async Task SaveChangesAndQueries_Succeed_WhenBroadcastTransportFails()
        {
            _bus.FailPublish = true;
            var writer = await StartReplicaAsync(broadcast: true);
            Assert.Equal("v1", (await ReadAsync(writer)).Name);

            await RenameAsync(writer, "v2");
            await _bus.DeliverAsync();

            Assert.Equal("v2", (await ReadAsync(writer)).Name);
        }

        [Fact]
        public async Task SaveChangesAndQueries_Succeed_WhenDistributedCacheIsDown()
        {
            var replica = await StartReplicaAsync(broadcast: true, l2: new FailingDistributedCache());

            Assert.Equal("v1", (await ReadAsync(replica)).Name);

            await RenameAsync(replica, "v2");
            await _bus.DeliverAsync();

            Assert.Equal("v2", (await ReadAsync(replica)).Name);
        }

        // Explicit transaction: the old row may be re-cached by another replica while the transaction is open;
        // the broadcast after commit must remove it.
        [Fact]
        public async Task ExplicitTransaction_BroadcastsAfterCommit_SoAnOtherReplicaDoesNotKeepTheOldRow()
        {
            var writer = await StartReplicaAsync(broadcast: true);
            var reader = await StartReplicaAsync(broadcast: true);

            await using (var context = CreateContext(writer))
            {
                await using var transaction = await context.Database.BeginTransactionAsync(Ct);
                var thing = await context.Things.FirstAsync(t => t.Id == 1, Ct);
                thing.Name = "v2";
                await context.SaveChangesAsync(Ct);

                Assert.Empty(_bus.NamesAtPublish); // SaveChanges inside the transaction announces nothing
                Assert.Equal("v1", (await ReadAsync(reader)).Name); // cold cache: the old row is cached now

                await transaction.CommitAsync(Ct);
            }

            await _bus.DeliverAsync();

            Assert.Equal(["v2"], _bus.NamesAtPublish); // published once, after the commit
            Assert.Equal("v2", (await ReadAsync(reader)).Name);
        }

        [Fact]
        public void ExplicitTransaction_SynchronousApis_BroadcastAfterCommit()
        {
            var writer = StartReplicaAsync(broadcast: true).GetAwaiter().GetResult();

            using (var context = CreateContext(writer))
            {
                using var transaction = context.Database.BeginTransaction();
                var thing = context.Things.First(t => t.Id == 1);
                thing.Name = "v2";
                context.SaveChanges();

                Assert.Empty(_bus.NamesAtPublish);

                transaction.Commit();
            }

            Assert.Equal(["v2"], _bus.NamesAtPublish);
        }

        [Fact]
        public async Task RolledBackTransaction_PublishesNothing()
        {
            var writer = await StartReplicaAsync(broadcast: true);
            var reader = await StartReplicaAsync(broadcast: true);
            Assert.Equal("v1", (await ReadAsync(reader)).Name);

            await using (var context = CreateContext(writer))
            {
                await using var transaction = await context.Database.BeginTransactionAsync(Ct);
                var thing = await context.Things.FirstAsync(t => t.Id == 1, Ct);
                thing.Name = "v2";
                await context.SaveChangesAsync(Ct);

                await transaction.RollbackAsync(Ct);
            }

            await _bus.DeliverAsync();

            Assert.Empty(_bus.NamesAtPublish);
            Assert.Equal("v1", (await ReadAsync(reader)).Name);
        }

        [Fact]
        public async Task DisposedUncommittedTransaction_PublishesNothing()
        {
            var writer = await StartReplicaAsync(broadcast: true);

            await using (var context = CreateContext(writer))
            {
                await using var transaction = await context.Database.BeginTransactionAsync(Ct);
                var thing = await context.Things.FirstAsync(t => t.Id == 1, Ct);
                thing.Name = "v2";
                await context.SaveChangesAsync(Ct);
            }

            Assert.Empty(_bus.NamesAtPublish);
        }

        [Fact]
        public async Task FailedSaveChanges_PublishesNothing()
        {
            var writer = await StartReplicaAsync(broadcast: true);

            await using (var context = CreateContext(writer))
            {
                context.Things.Add(new Thing { Id = 1, Name = "duplicate key" });

                await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
            }

            Assert.Empty(_bus.NamesAtPublish);
        }

        [Fact]
        public async Task ImplicitSaveChanges_PublishesExactlyOnce_AfterCommit()
        {
            var writer = await StartReplicaAsync(broadcast: true);

            await RenameAsync(writer, "v2");
            await _bus.DeliverAsync();

            Assert.Equal(["v2"], _bus.NamesAtPublish);
        }

        [Fact]
        public async Task ImplicitSaveChangesWithSeveralStatements_PublishesExactlyOnce_AfterCommit()
        {
            var writer = await StartReplicaAsync(broadcast: true);

            await using (var context = CreateContext(writer))
            {
                (await context.Things.FirstAsync(t => t.Id == 1, Ct)).Name = "v2";
                context.Things.Add(new Thing { Id = 2, Name = "other" });
                await context.SaveChangesAsync(Ct);
            }

            Assert.Equal(["v2"], _bus.NamesAtPublish);
        }

        [Fact]
        public void SynchronousSaveChanges_PublishesExactlyOnce_AfterCommit()
        {
            var writer = StartReplicaAsync(broadcast: true).GetAwaiter().GetResult();

            using (var context = CreateContext(writer))
            {
                context.Things.First(t => t.Id == 1).Name = "v2";
                context.SaveChanges();
            }

            Assert.Equal(["v2"], _bus.NamesAtPublish);
        }

        [Fact]
        public async Task WriteOutsideSaveChanges_IsPublishedAtOnce()
        {
            var writer = await StartReplicaAsync(broadcast: true);
            var reader = await StartReplicaAsync(broadcast: true);
            Assert.Equal("v1", (await ReadAsync(reader)).Name);

            await using (var context = CreateContext(writer))
            {
                await context.Things.Where(t => t.Id == 1).ExecuteUpdateAsync(set => set.SetProperty(t => t.Name, "v2"), Ct);
            }

            await _bus.DeliverAsync();

            Assert.Equal(["v2"], _bus.NamesAtPublish);
            Assert.Equal("v2", (await ReadAsync(reader)).Name);
        }

        [Fact]
        public async Task ReusedContext_DoesNotLeakPendingTagsBetweenUnitsOfWork()
        {
            var writer = await StartReplicaAsync(broadcast: true);

            await using var context = CreateContext(writer);
            await using (var transaction = await context.Database.BeginTransactionAsync(Ct))
            {
                (await context.Things.FirstAsync(t => t.Id == 1, Ct)).Name = "v2";
                await context.SaveChangesAsync(Ct);
                await transaction.RollbackAsync(Ct);
            }

            context.ChangeTracker.Clear();
            (await context.Things.FirstAsync(t => t.Id == 1, Ct)).Name = "v3";
            await context.SaveChangesAsync(Ct);

            Assert.Equal(["v3"], _bus.NamesAtPublish);
        }

        [Fact]
        public async Task ClearAll_IsBroadcast()
        {
            var writer = await StartReplicaAsync(broadcast: true);
            var reader = await StartReplicaAsync(broadcast: true);
            Assert.Equal("v1", (await ReadAsync(reader)).Name);

            await RenameInDatabaseOnlyAsync("v2");

            writer.GetRequiredService<IEFCacheServiceProvider>().ClearAllCachedEntries();
            await _bus.DeliverAsync();

            Assert.Equal("v2", (await ReadAsync(reader)).Name);
        }

        [Fact]
        public async Task Reconnect_ClearsTheLocalCache()
        {
            var reader = await StartReplicaAsync(broadcast: true);
            Assert.Equal("v1", (await ReadAsync(reader)).Name);
            await RenameInDatabaseOnlyAsync("v2"); // the invalidation for this change was "lost"

            await _bus.ReconnectAsync();

            Assert.Equal("v2", (await ReadAsync(reader)).Name);
        }

        private async Task<ServiceProvider> StartReplicaAsync(bool broadcast, IDistributedCache? l2 = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(l2 ?? _sharedL2);
            services.AddWkfloEfSecondLevelCache();

            // Registered last, so it replaces the default no-op transport.
            if (broadcast)
            {
                services.AddSingleton(_bus);
                services.AddSingleton<IEfCacheInvalidationTransport, InMemoryTransport>();
            }

            services.AddDbContext<ThingDbContext>((sp, options) => options
                .UseSqlite(_connectionString)
                .AddInterceptors(
                    sp.GetRequiredService<EfCacheInvalidationInterceptor>(),
                    sp.GetRequiredService<SecondLevelCacheInterceptor>()));

            var provider = services.BuildServiceProvider();
            _providers.Add(provider);

            foreach (var hostedService in provider.GetServices<IHostedService>())
            {
                await hostedService.StartAsync(Ct);
            }

            if (broadcast)
            {
                await _bus.WaitForSubscriptionAsync(Ct);
            }

            return provider;
        }

        private static ThingDbContext CreateContext(ServiceProvider replica) =>
            replica.CreateScope().ServiceProvider.GetRequiredService<ThingDbContext>();

        private static async Task<Thing> ReadAsync(ServiceProvider replica)
        {
            await using var context = CreateContext(replica);

            return await context.Things.AsNoTracking().Cacheable().FirstAsync(thing => thing.Id == 1, Ct);
        }

        private static async Task RenameAsync(ServiceProvider replica, string name)
        {
            await using var context = CreateContext(replica);
            var thing = await context.Things.FirstAsync(t => t.Id == 1, Ct);
            thing.Name = name;

            await context.SaveChangesAsync(Ct);
        }

        // Changes the row without going through a cache-aware context, so no invalidation happens.
        private async Task RenameInDatabaseOnlyAsync(string name)
        {
            await using var context = new ThingDbContext(new DbContextOptionsBuilder<ThingDbContext>().UseSqlite(_connectionString).Options);
            await context.Database.ExecuteSqlRawAsync("UPDATE Things SET Name = {0}", [name], Ct);
        }

        private sealed class Thing : IEquatable<Thing>
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public Guid Key { get; set; }
            public decimal Amount { get; set; }
            public bool Active { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public byte[] Payload { get; set; } = [];

            public bool Equals(Thing? other) =>
                other is not null && Id == other.Id && Name == other.Name && Key == other.Key && Amount == other.Amount
                && Active == other.Active && CreatedAt == other.CreatedAt && Payload.SequenceEqual(other.Payload);

            public override bool Equals(object? obj) => Equals(obj as Thing);

            public override int GetHashCode() => Id;
        }

        private sealed class ThingDbContext(DbContextOptions<ThingDbContext> options) : DbContext(options)
        {
            public DbSet<Thing> Things => Set<Thing>();
        }

        /// <summary>Stands in for Redis pub/sub; deliveries are tracked so a test can wait for the fire-and-forget publish.</summary>
        private sealed class InMemoryBus
        {
            private readonly List<Func<string, Task>> _handlers = [];
            private readonly List<Func<Task>> _reconnectHandlers = [];
            private readonly List<Task> _deliveries = [];
            private readonly SemaphoreSlim _subscriptions = new(0);

            public bool FailPublish { get; set; }

            /// <summary>Reads the row from the database; called at the moment of each publish.</summary>
            public Func<string>? NameAtPublish { get; set; }

            /// <summary>Name of the row in the database at the moment of each publish.</summary>
            public List<string> NamesAtPublish { get; } = [];

            public void Subscribe(Func<string, Task> handler, Func<Task> onReconnected)
            {
                lock (_deliveries)
                {
                    _handlers.Add(handler);
                    _reconnectHandlers.Add(onReconnected);
                }

                _subscriptions.Release();
            }

            /// <summary>
            /// BackgroundService.StartAsync returns before ExecuteAsync runs (.NET 10), so a replica may not be subscribed yet when it starts.
            /// </summary>
            public async Task WaitForSubscriptionAsync(CancellationToken cancellationToken)
            {
                if (!await _subscriptions.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
                {
                    throw new TimeoutException("The replica did not subscribe to the invalidation bus.");
                }
            }

            public Task PublishAsync(string message)
            {
                if (FailPublish)
                {
                    throw new InvalidOperationException("Redis is down");
                }

                lock (_deliveries)
                {
                    NamesAtPublish.Add(NameAtPublish?.Invoke() ?? "");
                    _deliveries.AddRange(_handlers.Select(handler => handler(message)));
                }

                return Task.CompletedTask;
            }

            public Task ReconnectAsync()
            {
                Func<Task>[] handlers;
                lock (_deliveries)
                {
                    handlers = [.. _reconnectHandlers];
                }

                return Task.WhenAll(handlers.Select(handler => handler()));
            }

            public async Task DeliverAsync()
            {
                Task[] pending;
                lock (_deliveries)
                {
                    pending = [.. _deliveries];
                }

                await Task.WhenAll(pending);
            }
        }

        private sealed class InMemoryTransport(InMemoryBus bus) : IEfCacheInvalidationTransport
        {
            public Task PublishAsync(string message, CancellationToken cancellationToken) => bus.PublishAsync(message);

            public Task SubscribeAsync(Func<string, Task> handler, Func<Task> onReconnected, CancellationToken cancellationToken)
            {
                bus.Subscribe(handler, onReconnected);
                return Task.CompletedTask;
            }
        }

        private sealed class FailingDistributedCache : IDistributedCache
        {
            private static InvalidOperationException Down() => new("Redis is down");

            public byte[]? Get(string key) => throw Down();
            public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();
            public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
            public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
            public void Refresh(string key) => throw Down();
            public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();
            public void Remove(string key) => throw Down();
            public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
        }
    }
}
