using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
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
    /// Regression test: republishing an already-published TenantTool that hasn't changed since its last
    /// publish must still bump <see cref="PublishedTenantTool.LastUpdatedOn"/> and produce an Update
    /// <see cref="AuditLogEntry"/>.
    /// </summary>
    /// <remarks>
    /// Before <see cref="PublishTenantToolCommandHandler"/> explicitly stamped <c>LastUpdatedOn</c> in its
    /// "reuse existing published tool" branch, <c>_mapper.Map(tenantTool, publishedTenantTool)</c> produced
    /// zero real property differences for an unchanged tool, so EF Core's change tracker left the entity
    /// <c>Unchanged</c>. Neither <c>MasterDataDbContext.SaveChangesAsync</c>'s own bookkeeping loop (which
    /// only stamps <c>LastUpdatedOn</c> for entries it sees as <c>Modified</c>) nor
    /// <c>AppendAuditLogEntries</c> (whose <c>ResolveAuditAction</c> returns <see langword="null"/> for
    /// <c>Unchanged</c>) ever ran for it — a deliberate republish action left zero trace: no timestamp bump,
    /// no audit row. This exercises the real handler against a real SQLite-backed
    /// <see cref="MasterDataDbContext"/> (not a mock), since the bug is about EF Core change-tracking state,
    /// which a mocked <c>IMasterDataDbContext</c> can't reveal.
    /// </remarks>
    public class PublishTenantToolCommandHandlerAuditRegressionTests
    {
        private static readonly DateTimeOffset FirstPublishNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset RepublishNow = FirstPublishNow.AddDays(1);

        [Fact]
        public async Task RepublishingAnUnchangedTool_StillBumpsLastUpdatedOn_AndWritesAnUpdateAuditEntry()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };

            var firstProviderMock = new Mock<IDateTimeProvider>();
            firstProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(FirstPublishNow);

            int tenantToolId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, firstProviderMock.Object))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

                var tenantTool = new TenantTool
                {
                    TenantId = tenant.Id,
                    Name = "My Tool",
                    Type = "HTTP",
                    Config = "{}"
                };
                setupDb.TenantTools.Add(tenantTool);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantToolId = tenantTool.Id;
            }

            // --- First publish: creates the PublishedTenantTool ---
            await using (var firstPublishDb = new SqliteTestMasterDataDbContext(options, firstProviderMock.Object))
            {
                firstPublishDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new PublishTenantToolCommandHandler(firstPublishDb, mapper, tenantContext, firstProviderMock.Object);
                var result = await handler.Handle(new PublishTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolPublishResult>>(result);
            }

            // --- Republish: same TenantTool, no field changes at all since the first publish — the
            // regression scenario. Uses a different IDateTimeProvider "now" so LastUpdatedOn's before/after
            // is unambiguous.
            var republishProviderMock = new Mock<IDateTimeProvider>();
            republishProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(RepublishNow);

            await using (var republishDb = new SqliteTestMasterDataDbContext(options, republishProviderMock.Object))
            {
                republishDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new PublishTenantToolCommandHandler(republishDb, mapper, tenantContext, republishProviderMock.Object);
                var result = await handler.Handle(new PublishTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolPublishResult>>(result);
            }

            // --- Assert: the republish bumped LastUpdatedOn and produced its own Update audit row ---
            await using var verifyDb = new SqliteTestMasterDataDbContext(options, republishProviderMock.Object);
            var publishedTool = await verifyDb.PublishedTenantTools.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(RepublishNow, publishedTool.LastUpdatedOn);

            var republishAuditEntry = await verifyDb.AuditLogEntries.SingleAsync(
                a => a.EntityType == "PublishedTenantTool" && a.CorrelationId == "corr-2",
                TestContext.Current.CancellationToken);
            Assert.Equal(CommonConsts.AuditActionConsts.Update, republishAuditEntry.Action);
        }
    }
}
