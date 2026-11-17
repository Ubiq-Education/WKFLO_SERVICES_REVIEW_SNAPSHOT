using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.DeleteTenantTool
{
    /// <summary>
    /// Regression test for a bug found during manual testing of the US 7256 audit feature: deleting a
    /// TenantTool only soft-deleted the TenantTool row itself, unlike the Agent side's
    /// DeleteTenantAgentCommandHandler, which also cascades to the linked PublishedTenantAgent. Because the
    /// PublishedTenantTool (the entity the chat widget actually serves from) was left untouched — and, before
    /// this fix, was never even loaded — deleting a TenantTool that had already been shared/published left
    /// the published copy live and produced only a single Delete AuditLogEntry, instead of the two rows
    /// (TenantTool + PublishedTenantTool) the Agent-side equivalent produces.
    /// </summary>
    /// <remarks>
    /// Fixed by mirroring DeleteTenantAgentCommandHandler's cascade exactly: look up the PublishedTenantTool
    /// via CreatedByTenantToolId == command.Id and, if found, soft-delete it in the same SaveChanges call so
    /// MasterDataDbContext's audit interceptor picks up both entities' state changes in one pass. This test
    /// exercises the real handler against a real SQLite-backed MasterDataDbContext (not a mock) specifically
    /// to prove the AuditLogEntry count/shape claim, which a mocked IMasterDataDbContext cannot demonstrate.
    /// </remarks>
    public class DeleteTenantToolCommandHandlerAuditRegressionTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task DeletingATenantTool_WithALinkedPublishedTool_SoftDeletesBoth_AndWritesTwoDeleteAuditEntries()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);
            var dateTimeProvider = dateTimeProviderMock.Object;

            var mapper = WkfloMapperExtensions.CreateMapper();

            int tenantToolId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
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

                var publishedTool = mapper.Map<PublishedTenantTool>(tenantTool);
                publishedTool.TenantId = tenant.Id;
                publishedTool.CreatedByTenantToolId = tenantTool.Id;
                setupDb.PublishedTenantTools.Add(publishedTool);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            // --- Delete the TenantTool, which should cascade to its linked PublishedTenantTool ---
            await using (var deleteDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                deleteDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new DeleteTenantToolCommandHandler(deleteDb, mapper);
                var result = await handler.Handle(new DeleteTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolDeleteResult>>(result);
            }

            // --- Assert: both rows are soft-deleted. Tool has a global query filter (!IsDeleted), so we
            // must IgnoreQueryFilters() here to be able to see the very rows we just soft-deleted. ---
            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var persistedTenantTool = await verifyDb.TenantTools.IgnoreQueryFilters()
                .SingleAsync(t => t.Id == tenantToolId, TestContext.Current.CancellationToken);
            Assert.True(persistedTenantTool.IsDeleted);

            var persistedPublishedTool = await verifyDb.PublishedTenantTools.IgnoreQueryFilters()
                .SingleAsync(p => p.CreatedByTenantToolId == tenantToolId, TestContext.Current.CancellationToken);
            Assert.True(persistedPublishedTool.IsDeleted);

            // --- Assert: exactly two Delete AuditLogEntry rows were written for this correlation, one per
            // entity, matching the Agent side's DeleteTenantAgentCommandHandler behavior ---
            var auditEntries = await verifyDb.AuditLogEntries
                .Where(a => a.CorrelationId == "corr-1")
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, auditEntries.Count);
            Assert.All(auditEntries, a => Assert.Equal(CommonConsts.AuditActionConsts.Delete, a.Action));

            Assert.Contains(auditEntries, a => a.EntityType == "TenantTool" && a.EntityId == tenantToolId.ToString());
            Assert.Contains(auditEntries, a => a.EntityType == "PublishedTenantTool" && a.EntityId == persistedPublishedTool.Id.ToString());
        }

        [Fact]
        public async Task DeletingATenantTool_WithNoLinkedPublishedTool_DoesNotThrow_AndWritesOneDeleteAuditEntry()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);
            var dateTimeProvider = dateTimeProviderMock.Object;

            var mapper = WkfloMapperExtensions.CreateMapper();

            int tenantToolId;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "T1" };
                setupDb.Tenants.Add(tenant);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

                // Never shared/published — no PublishedTenantTool row exists for this TenantTool at all.
                var tenantTool = new TenantTool
                {
                    TenantId = tenant.Id,
                    Name = "Unpublished Tool",
                    Type = "HTTP",
                    Config = "{}"
                };
                setupDb.TenantTools.Add(tenantTool);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantToolId = tenantTool.Id;
            }

            await using (var deleteDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                deleteDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new DeleteTenantToolCommandHandler(deleteDb, mapper);

                // Must not throw a null-reference (or any other exception) when there's no linked
                // PublishedTenantTool to cascade to.
                var result = await handler.Handle(new DeleteTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolDeleteResult>>(result);
            }

            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var persistedTenantTool = await verifyDb.TenantTools.IgnoreQueryFilters()
                .SingleAsync(t => t.Id == tenantToolId, TestContext.Current.CancellationToken);
            Assert.True(persistedTenantTool.IsDeleted);

            var auditEntries = await verifyDb.AuditLogEntries
                .Where(a => a.CorrelationId == "corr-2")
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Single(auditEntries);
            Assert.Equal("TenantTool", auditEntries[0].EntityType);
            Assert.Equal(CommonConsts.AuditActionConsts.Delete, auditEntries[0].Action);
        }
    }
}
