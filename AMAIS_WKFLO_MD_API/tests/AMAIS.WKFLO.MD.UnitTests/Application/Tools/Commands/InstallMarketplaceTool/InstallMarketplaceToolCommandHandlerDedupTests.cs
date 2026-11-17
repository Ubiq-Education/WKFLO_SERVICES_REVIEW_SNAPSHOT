using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.InstallMarketplaceTool
{
    /// <summary>
    /// Tool-side mirror of InstallMarketplaceAgentCommandHandlerDedupTests - see that class's doc
    /// comment for the full rationale. Uses a real SQLite-backed MasterDataDbContext since these
    /// scenarios need real relational COUNT()/FK behavior, not just mocked DbSets.
    /// </summary>
    public class InstallMarketplaceToolCommandHandlerDedupTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static async Task<(DbContextOptions<MasterDataDbContext> Options, int MarketplaceToolId)> SeedAsync(string toolName = "Test Marketplace Tool")
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var providerMock = new Mock<IDateTimeProvider>();
            providerMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            int marketplaceToolId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, providerMock.Object))
            {
                // MarketplaceTool.TenantId is NOT NULL (tracks which tenant originally shared it) - set
                // via the Tenant navigation so EF resolves the FK even though both rows save in one batch.
                var tenant = new Tenant { Name = "Test" };
                setupDb.Tenants.Add(tenant);

                var marketplaceTool = new MarketplaceTool
                {
                    Name = toolName,
                    Description = "Desc",
                    Type = "API",
                    Config = "{}",
                    Tenant = tenant
                };
                setupDb.MarketplaceTools.Add(marketplaceTool);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                marketplaceToolId = marketplaceTool.Id;
            }

            return (options, marketplaceToolId);
        }

        [Fact]
        public async Task Handle_NotPreviouslyInstalled_CreatesNewTenantToolWithFkAndCountBecomesOne()
        {
            var (options, marketplaceToolId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();
            var tenantContext = new TenantContext { TenantName = "Test" };

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceToolCommandHandler(db, mapper, mediator, tenantContext);
                var result = await handler.Handle(new InstallMarketplaceToolCommand { Id = marketplaceToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var tenantTools = await verifyDb.TenantTools.ToListAsync(TestContext.Current.CancellationToken);
            var tenantTool = Assert.Single(tenantTools);
            Assert.Equal(marketplaceToolId, tenantTool.InstalledFromMarketplaceToolId);

            var count = await verifyDb.TenantTools.CountAsync(
                tt => tt.InstalledFromMarketplaceToolId == marketplaceToolId && !tt.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Handle_AlreadyInstalledBySameTenant_ReturnsExistingWithoutDuplicateRowOrCountIncrease()
        {
            var (options, marketplaceToolId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();
            var tenantContext = new TenantContext { TenantName = "Test" };

            int firstId;
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceToolCommandHandler(db, mapper, mediator, tenantContext);
                var firstResult = (SuccessResult<TenantToolCreateResult>)await handler.Handle(
                    new InstallMarketplaceToolCommand { Id = marketplaceToolId }, TestContext.Current.CancellationToken);
                firstId = firstResult.Data.Id;
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceToolCommandHandler(db, mapper, mediator, tenantContext);
                var secondResult = (SuccessResult<TenantToolCreateResult>)await handler.Handle(
                    new InstallMarketplaceToolCommand { Id = marketplaceToolId }, TestContext.Current.CancellationToken);

                Assert.Equal(firstId, secondResult.Data.Id);
                Assert.Equal("Test Marketplace Tool", secondResult.Data.Name); // no "(Copy N)" suffix
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var tenantTools = await verifyDb.TenantTools.ToListAsync(TestContext.Current.CancellationToken);
            Assert.Single(tenantTools); // still exactly one row - no duplicate created

            var count = await verifyDb.TenantTools.CountAsync(
                tt => tt.InstalledFromMarketplaceToolId == marketplaceToolId && !tt.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(1, count); // count did NOT increase on the second install
        }

        [Fact]
        public async Task Handle_DifferentToolSameName_StillCreatesCopyWithSuffix()
        {
            var (options, firstToolId) = await SeedAsync("Shared Name");
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();
            var tenantContext = new TenantContext { TenantName = "Test" };

            int secondToolId;
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = await db.Tenants.FirstAsync(TestContext.Current.CancellationToken);
                var secondTool = new MarketplaceTool { Name = "Shared Name", Description = "Desc 2", Type = "API", Config = "{}", Tenant = tenant };
                db.MarketplaceTools.Add(secondTool);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
                secondToolId = secondTool.Id;
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceToolCommandHandler(db, mapper, mediator, tenantContext);
                await handler.Handle(new InstallMarketplaceToolCommand { Id = firstToolId }, TestContext.Current.CancellationToken);
            }

            SuccessResult<TenantToolCreateResult> secondResult;
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceToolCommandHandler(db, mapper, mediator, tenantContext);
                secondResult = (SuccessResult<TenantToolCreateResult>)await handler.Handle(
                    new InstallMarketplaceToolCommand { Id = secondToolId }, TestContext.Current.CancellationToken);
            }

            Assert.Equal("Shared Name (Copy 1)", secondResult.Data.Name);
        }

        [Fact]
        public async Task Handle_MultipleTenants_EachGetsOwnRowAndLiveCountReflectsAll()
        {
            var (options, marketplaceToolId) = await SeedAsync();
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
                var handler = new InstallMarketplaceToolCommandHandler(db, mapper, mediator, new TenantContext { TenantName = "Test" });
                await handler.Handle(new InstallMarketplaceToolCommand { Id = marketplaceToolId }, TestContext.Current.CancellationToken);
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceToolCommandHandler(db, mapper, mediator, new TenantContext { TenantName = "OtherTenant" });
                await handler.Handle(new InstallMarketplaceToolCommand { Id = marketplaceToolId }, TestContext.Current.CancellationToken);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var count = await verifyDb.TenantTools.CountAsync(
                tt => tt.InstalledFromMarketplaceToolId == marketplaceToolId && !tt.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task Handle_AfterSoftDelete_LiveCountExcludesDeletedRow()
        {
            var (options, marketplaceToolId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            var mapper = WkfloMapperExtensions.CreateMapper();
            var mediator = Mock.Of<IWkfloMediator>();

            int tenantToolId;
            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var handler = new InstallMarketplaceToolCommandHandler(db, mapper, mediator, new TenantContext { TenantName = "Test" });
                var result = (SuccessResult<TenantToolCreateResult>)await handler.Handle(
                    new InstallMarketplaceToolCommand { Id = marketplaceToolId }, TestContext.Current.CancellationToken);
                tenantToolId = result.Data.Id;
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenantTool = await db.TenantTools.SingleAsync(tt => tt.Id == tenantToolId, TestContext.Current.CancellationToken);
                tenantTool.IsDeleted = true;
                db.TenantTools.Update(tenantTool);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var count = await verifyDb.TenantTools.CountAsync(
                tt => tt.InstalledFromMarketplaceToolId == marketplaceToolId && !tt.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Handle_ZeroInstalls_LiveCountIsZero()
        {
            var (options, marketplaceToolId) = await SeedAsync();
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var count = await verifyDb.TenantTools.CountAsync(
                tt => tt.InstalledFromMarketplaceToolId == marketplaceToolId && !tt.IsDeleted, TestContext.Current.CancellationToken);
            Assert.Equal(0, count);
        }
    }
}
