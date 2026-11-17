using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.PublishTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.PublishTenantTool
{
    /// <summary>
    /// Tool-side mirror of PublishTenantAgentCommandHandlerInstalledFromMarketplaceAgentIdTests - see
    /// that class's doc comment for the full rationale.
    /// </summary>
    public class PublishTenantToolCommandHandlerInstalledFromMarketplaceToolIdTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Handle_FirstPublish_CopiesInstalledFromMarketplaceToolIdToPublishedSnapshot()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };
            var dateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);

            int tenantToolId;
            int marketplaceToolId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);

                var marketplaceTool = new MarketplaceTool
                {
                    Name = "Template Tool",
                    Description = "Desc",
                    Type = "API",
                    Config = "{}",
                    Tenant = tenant
                };
                setupDb.MarketplaceTools.Add(marketplaceTool);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                marketplaceToolId = marketplaceTool.Id;

                var tenantTool = new TenantTool
                {
                    TenantId = tenant.Id,
                    Name = "My Installed Tool",
                    Description = "A tool",
                    Type = "API",
                    Config = "{}",
                    InstalledFromMarketplaceToolId = marketplaceToolId
                };
                setupDb.TenantTools.Add(tenantTool);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantToolId = tenantTool.Id;
            }

            await using (var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                db.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new PublishTenantToolCommandHandler(db, mapper, tenantContext, dateTimeProvider);
                var result = await handler.Handle(new PublishTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolPublishResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var publishedTool = await verifyDb.PublishedTenantTools.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(marketplaceToolId, publishedTool.InstalledFromMarketplaceToolId);
        }
    }
}
