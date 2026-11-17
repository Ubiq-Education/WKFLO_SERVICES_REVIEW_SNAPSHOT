using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.InstallMarketplaceAgent
{
    /// <summary>
    /// Covers the real install-dedup behavior added for WI 7312: a tenant re-installing the SAME
    /// MarketplaceAgent must reuse the existing TenantAgent rather than creating a duplicate (the
    /// pre-existing name-collision "(Copy N)" loop is for a DIFFERENT, unrelated agent that happens to
    /// share a name — kept working here too), and MarketplaceAgent.Installations is now a live COUNT
    /// of non-deleted TenantAgents installed from it, not a stored counter. Uses a real SQLite-backed
    /// MasterDataDbContext (same approach as ShareTenantAgentCommandHandlerIconUrlTests) since these
    /// scenarios need real relational COUNT()/FK behavior, not just mocked DbSets.
    /// </summary>
    public class InstallMarketplaceAgentCommandHandlerDedupTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static async Task<(DbContextOptions<MasterDataDbContext> Options, int MarketplaceAgentId)> SeedAsync(string agentName = "Test Marketplace Agent")
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var providerMock = new Mock<IDateTimeProvider>();
            providerMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            int marketplaceAgentId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, providerMock.Object))
            {
                // MarketplaceAgent.TenantId is NOT NULL (tracks which tenant originally shared it) - set
                // via the Tenant navigation so EF resolves the FK even though both rows save in one batch.
                var tenant = new Tenant { Name = "Test" };
                setupDb.Tenants.Add(tenant);

                var marketplaceAgent = new MarketplaceAgent
                {
                    Name = agentName,
                    Description = "Desc",
                    SystemPrompt = "You are a helpful assistant.",
                    AgentTools = new List<AgentTool>(),
                    Tenant = tenant
                };
                setupDb.MarketplaceAgents.Add(marketplaceAgent);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                marketplaceAgentId = marketplaceAgent.Id;
            }

            return (options, marketplaceAgentId);
        }

        [Fact]
        public async Task Handle_NotPreviouslyInstalled_CreatesNewTenantAgentWithFkAndCountBecomesOne()
        {
            var (options, marketplaceAgentId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();
            var tenantContext = new TenantContext { TenantName = "Test" };

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, tenantContext);
                var result = await handler.Handle(new InstallMarketplaceAgentCommand { Id = marketplaceAgentId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantAgentCreateResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var tenantAgents = await verifyDb.TenantAgents.ToListAsync(TestContext.Current.CancellationToken);
            var tenantAgent = Assert.Single(tenantAgents);
            Assert.Equal(marketplaceAgentId, tenantAgent.InstalledFromMarketplaceAgentId);

            var count = await verifyDb.TenantAgents.CountAsync(
                ta => ta.InstalledFromMarketplaceAgentId == marketplaceAgentId && !ta.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Handle_AlreadyInstalledBySameTenant_ReturnsExistingWithoutDuplicateRowOrCountIncrease()
        {
            var (options, marketplaceAgentId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();
            var tenantContext = new TenantContext { TenantName = "Test" };

            int firstId;
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, tenantContext);
                var firstResult = (SuccessResult<TenantAgentCreateResult>)await handler.Handle(
                    new InstallMarketplaceAgentCommand { Id = marketplaceAgentId }, TestContext.Current.CancellationToken);
                firstId = firstResult.Data.Id;
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, tenantContext);
                var secondResult = (SuccessResult<TenantAgentCreateResult>)await handler.Handle(
                    new InstallMarketplaceAgentCommand { Id = marketplaceAgentId }, TestContext.Current.CancellationToken);

                Assert.Equal(firstId, secondResult.Data.Id);
                Assert.Equal("Test Marketplace Agent", secondResult.Data.Name); // no "(Copy N)" suffix
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var tenantAgents = await verifyDb.TenantAgents.ToListAsync(TestContext.Current.CancellationToken);
            Assert.Single(tenantAgents); // still exactly one row - no duplicate created

            var count = await verifyDb.TenantAgents.CountAsync(
                ta => ta.InstalledFromMarketplaceAgentId == marketplaceAgentId && !ta.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(1, count); // count did NOT increase on the second install
        }

        [Fact]
        public async Task Handle_DifferentAgentSameName_StillCreatesCopyWithSuffix()
        {
            // Two DIFFERENT MarketplaceAgents that happen to share a Name - genuinely unrelated, so the
            // pre-existing "(Copy N)" rename loop must still kick in (dedup only short-circuits when
            // InstalledFromMarketplaceAgentId matches the SAME source agent).
            var (options, firstAgentId) = await SeedAsync("Shared Name");
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();
            var tenantContext = new TenantContext { TenantName = "Test" };

            int secondAgentId;
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = await db.Tenants.FirstAsync(TestContext.Current.CancellationToken);
                var secondAgent = new MarketplaceAgent
                {
                    Name = "Shared Name",
                    Description = "Desc 2",
                    SystemPrompt = "Prompt 2",
                    AgentTools = new List<AgentTool>(),
                    Tenant = tenant
                };
                db.MarketplaceAgents.Add(secondAgent);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
                secondAgentId = secondAgent.Id;
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, tenantContext);
                await handler.Handle(new InstallMarketplaceAgentCommand { Id = firstAgentId }, TestContext.Current.CancellationToken);
            }

            SuccessResult<TenantAgentCreateResult> secondResult;
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, tenantContext);
                secondResult = (SuccessResult<TenantAgentCreateResult>)await handler.Handle(
                    new InstallMarketplaceAgentCommand { Id = secondAgentId }, TestContext.Current.CancellationToken);
            }

            Assert.Equal("Shared Name (Copy 1)", secondResult.Data.Name);
        }

        [Fact]
        public async Task Handle_MultipleTenants_EachGetsOwnRowAndLiveCountReflectsAll()
        {
            var (options, marketplaceAgentId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.Tenants.Add(new Tenant { Name = "OtherTenant" });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, new TenantContext { TenantName = "Test" });
                await handler.Handle(new InstallMarketplaceAgentCommand { Id = marketplaceAgentId }, TestContext.Current.CancellationToken);
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, new TenantContext { TenantName = "OtherTenant" });
                await handler.Handle(new InstallMarketplaceAgentCommand { Id = marketplaceAgentId }, TestContext.Current.CancellationToken);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var count = await verifyDb.TenantAgents.CountAsync(
                ta => ta.InstalledFromMarketplaceAgentId == marketplaceAgentId && !ta.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task Handle_AfterSoftDelete_LiveCountExcludesDeletedRow()
        {
            var (options, marketplaceAgentId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();

            int tenantAgentId;
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceAgentCommandHandler(db, mapper, mediator, new TenantContext { TenantName = "Test" });
                var result = (SuccessResult<TenantAgentCreateResult>)await handler.Handle(
                    new InstallMarketplaceAgentCommand { Id = marketplaceAgentId }, TestContext.Current.CancellationToken);
                tenantAgentId = result.Data.Id;
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenantAgent = await db.TenantAgents.SingleAsync(ta => ta.Id == tenantAgentId, TestContext.Current.CancellationToken);
                tenantAgent.IsDeleted = true;
                db.TenantAgents.Update(tenantAgent);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var count = await verifyDb.TenantAgents.CountAsync(
                ta => ta.InstalledFromMarketplaceAgentId == marketplaceAgentId && !ta.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Handle_ZeroInstalls_LiveCountIsZero()
        {
            var (options, marketplaceAgentId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var count = await verifyDb.TenantAgents.CountAsync(
                ta => ta.InstalledFromMarketplaceAgentId == marketplaceAgentId && !ta.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(0, count);
        }
    }
}
