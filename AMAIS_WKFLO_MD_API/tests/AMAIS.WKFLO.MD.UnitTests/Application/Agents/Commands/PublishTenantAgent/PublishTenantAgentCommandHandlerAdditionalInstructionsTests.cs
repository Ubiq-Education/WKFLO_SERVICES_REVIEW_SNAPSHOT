using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.PublishTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.PublishTenantAgent
{
    /// <summary>
    /// WI 7318: <see cref="RunnableAgent.AdditionalInstructions"/> is declared on RunnableAgent so the
    /// TenantAgent→PublishedTenantAgent Mapster convention copies it on publish (and re-publish), while
    /// MarketplaceAgent (share/install) structurally cannot carry it. Uses a real SQLite-backed
    /// MasterDataDbContext so the real EF TPH column mapping and the real Mapster config are exercised.
    /// </summary>
    public class PublishTenantAgentCommandHandlerAdditionalInstructionsTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static async Task<(SqliteConnection Connection, DbContextOptions<MasterDataDbContext> Options)> CreateDatabaseAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>());
            await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            return (connection, options);
        }

        private static async Task<int> SeedTenantAgentAsync(DbContextOptions<MasterDataDbContext> options, IDateTimeProvider dateTimeProvider, string additionalInstructions)
        {
            await using var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var tenant = new Tenant { Name = "T1" };
            setupDb.Tenants.Add(tenant);
            await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

            var tenantAgent = new TenantAgent
            {
                TenantId = tenant.Id,
                Name = "Support Agent",
                Description = "An agent",
                SystemPrompt = "You are a helpful assistant.",
                AdditionalInstructions = additionalInstructions
            };
            setupDb.TenantAgents.Add(tenantAgent);
            await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            return tenantAgent.Id;
        }

        private static async Task<Result> PublishAsync(DbContextOptions<MasterDataDbContext> options, IDateTimeProvider dateTimeProvider, int tenantAgentId)
        {
            await using var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
            var handler = new PublishTenantAgentCommandHandler(db, WkfloMapperExtensions.CreateMapper(),
                new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" }, Mock.Of<IWkfloMediator>(), dateTimeProvider);
            return await handler.Handle(new PublishTenantAgentCommand { Id = tenantAgentId }, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Handle_FirstPublish_CopiesAdditionalInstructionsToPublishedSnapshot()
        {
            var (connection, options) = await CreateDatabaseAsync();
            await using var _ = connection;
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var tenantAgentId = await SeedTenantAgentAsync(options, dateTimeProvider, "Keep answers under 100 words.");

            var result = await PublishAsync(options, dateTimeProvider, tenantAgentId);

            var success = Assert.IsType<SuccessResult<TenantAgentPublishResult>>(result);
            Assert.Equal("Keep answers under 100 words.", success.Data.AdditionalInstructions);

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var published = await verifyDb.PublishedTenantAgents.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Keep answers under 100 words.", published.AdditionalInstructions);
        }

        [Fact]
        public async Task Handle_Republish_PropagatesChangedAndClearedValues()
        {
            var (connection, options) = await CreateDatabaseAsync();
            await using var _ = connection;
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var tenantAgentId = await SeedTenantAgentAsync(options, dateTimeProvider, "First version.");
            await PublishAsync(options, dateTimeProvider, tenantAgentId);

            async Task SetTenantAgentInstructionsAsync(string value)
            {
                await using var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-x");
                var agent = await db.TenantAgents.SingleAsync(a => a.Id == tenantAgentId, TestContext.Current.CancellationToken);
                agent.AdditionalInstructions = value;
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            async Task<string> ReadPublishedAsync()
            {
                await using var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
                return (await db.PublishedTenantAgents.SingleAsync(TestContext.Current.CancellationToken)).AdditionalInstructions;
            }

            await SetTenantAgentInstructionsAsync("Second version.");
            await PublishAsync(options, dateTimeProvider, tenantAgentId);
            Assert.Equal("Second version.", await ReadPublishedAsync());

            // A cleared draft must clear the published snapshot too (no stale admin text left running).
            await SetTenantAgentInstructionsAsync(null);
            await PublishAsync(options, dateTimeProvider, tenantAgentId);
            Assert.Null(await ReadPublishedAsync());
        }

        [Fact]
        public async Task EfModel_MapsAdditionalInstructionsToSingleSharedAgentColumn()
        {
            var (connection, options) = await CreateDatabaseAsync();
            await using var _ = connection;
            await using var db = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>());

            foreach (var type in new[] { typeof(TenantAgent), typeof(PublishedTenantAgent) })
            {
                var property = db.Model.FindEntityType(type)!.FindProperty(nameof(RunnableAgent.AdditionalInstructions));
                Assert.NotNull(property);
                Assert.Equal("AdditionalInstructions", property!.GetColumnName(StoreObjectIdentifier.Table("Agent", null)));
            }
        }

        [Fact]
        public void MarketplaceAgent_CannotCarryAdditionalInstructions()
        {
            // Share (PublishedTenantAgent → MarketplaceAgent) and install (MarketplaceAgent → TenantAgent)
            // must never move one tenant's text to another tenant; that is guaranteed structurally by the
            // property not existing on MarketplaceAgent / the Agent base.
            Assert.Null(typeof(MarketplaceAgent).GetProperty(nameof(RunnableAgent.AdditionalInstructions)));

            var mapper = WkfloMapperExtensions.CreateMapper();
            var shared = mapper.Map<MarketplaceAgent>(new PublishedTenantAgent
            {
                Name = "Support Agent",
                Description = "An agent",
                SystemPrompt = "You are a helpful assistant.",
                AdditionalInstructions = "Always use a warm, welcoming tone."
            });
            var installed = mapper.Map<TenantAgent>(shared);

            Assert.Null(installed.AdditionalInstructions);
        }
    }
}
