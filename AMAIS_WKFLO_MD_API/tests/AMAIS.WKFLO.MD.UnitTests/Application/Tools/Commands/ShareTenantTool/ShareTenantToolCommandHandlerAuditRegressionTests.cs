using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.ShareTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.ShareTenantTool
{
    /// <summary>
    /// Regression test for a bug found during manual testing of the US 7256 audit feature: sharing a tenant
    /// tool produced an <see cref="AuditLogEntry"/> for <see cref="PublishedTenantTool"/> whose <c>Changes</c>
    /// JSON claimed <c>MarketplaceToolId</c> went from <c>null</c> to an EF Core temporary key placeholder
    /// (e.g. <c>-2147482647</c>) even though the real, final database column value was correct all along
    /// (reported against the "reuse an already-published tool" branch specifically; reproduced here — with
    /// the exact same placeholder-value shape — already on the very first share, i.e. the root cause is not
    /// specific to that branch).
    /// </summary>
    /// <remarks>
    /// Root cause: <see cref="ShareTenantToolCommandHandler"/> sets a one-to-one navigation —
    /// <c>publishedTool.MarketplaceTool = marketplaceTool;</c> — where <c>marketplaceTool</c>'s own primary
    /// key is database-generated and not yet known (either because it's genuinely new — the "else"/create
    /// branch, confirmed by this test — or, per the bug report, because it was loaded independently of
    /// <c>publishedTool</c> via a separate query rather than via <c>.Include()</c>, so EF Core's change
    /// tracker doesn't yet consider the pair's relationship state resolved). <c>MasterDataDbContext</c>'s
    /// audit code (<c>AppendAuditLogEntries</c>) reads <c>ChangeTracker.Entries()</c> — which forces a
    /// <c>DetectChanges()</c> pass — *before* calling <c>base.SaveChangesAsync</c>. If that pass lands mid
    /// one-to-one relationship fixup, EF Core can assign the dependent's FK property
    /// (<c>PublishedTenantTool.MarketplaceToolId</c>) a temporary placeholder value as bookkeeping, to be
    /// replaced with the real one once the principal's insert/resolution completes — normal, intentional EF
    /// Core internals, never meant to be observed outside them. The audit code captured that transient
    /// placeholder into a permanent record before <c>SaveChangesAsync</c> ever got a chance to resolve it to
    /// the real, correct, unchanged value.
    ///
    /// Fixed defensively in <c>MasterDataDbContext.BuildChangesJson</c> (skips any property where
    /// <c>PropertyEntry.IsTemporary</c> is true) so this bug class can't recur for any other FK/relationship
    /// fixup scenario, not just this one. This test exercises the exact handler code path (real Mapster
    /// mapping, real EF Core change tracking, real SQLite-backed <see cref="MasterDataDbContext"/>) rather
    /// than a mock, since the bug is a change-tracking timing issue a mock can't reveal.
    /// </remarks>
    public class ShareTenantToolCommandHandlerAuditRegressionTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task ReSharingAnAlreadyPublishedTool_DoesNotWriteASpuriousMarketplaceToolIdDiff()
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
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };

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

            // --- First share: creates the MarketplaceTool (the "else" branch) ---
            await using (var firstShareDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                firstShareDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantToolCommandHandler(firstShareDb, mapper, tenantContext, dateTimeProvider);
                var result = await handler.Handle(new ShareTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolShareResult>>(result);
            }

            // --- Second share ("re-share"): reuses the existing MarketplaceTool — the regression scenario ---
            await using (var reShareDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                reShareDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new ShareTenantToolCommandHandler(reShareDb, mapper, tenantContext, dateTimeProvider);
                var result = await handler.Handle(new ShareTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolShareResult>>(result);
            }

            // --- Assert: no audit entry from either pass may ever surface a MarketplaceToolId diff. The FK
            // is only ever set once via one-to-one relationship fixup (null -> a real value, established
            // before the row is ever read back), so a real change to it never legitimately happens in this
            // flow. Before the fix, this reproduced on the FIRST share already (not just the re-share): the
            // "else" branch's `publishedTool.MarketplaceTool = marketplaceTool;` triggers EF Core's one-to-one
            // fixup against a brand-new MarketplaceTool whose real database-generated Id isn't known yet,
            // and MasterDataDbContext.AppendAuditLogEntries reads ChangeTracker.Entries() — forcing
            // DetectChanges() — before SaveChangesAsync resolves that temp key into the real one.
            await using var verifyDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var allEntries = await verifyDb.AuditLogEntries.ToListAsync(TestContext.Current.CancellationToken);

            var publishedToolEntries = allEntries.Where(e => e.EntityType == "PublishedTenantTool").ToList();
            Assert.NotEmpty(publishedToolEntries); // sanity: the scenario actually exercised this entity

            foreach (var entry in allEntries)
            {
                if (entry.Changes is null)
                    continue;

                Assert.DoesNotContain("MarketplaceToolId", entry.Changes);
                AssertNoTemporaryPlaceholderValues(entry.Changes);
            }

            // The MarketplaceTool's own Create entry now records its initial values too (Create diffs were
            // added after this bug was first fixed) — confirm that path is equally free of temporary
            // placeholders, since a newly-Added entity's own not-yet-resolved database-generated Id (and any
            // FK on it pointing at another not-yet-saved entity in the same graph) is exactly the kind of
            // property most likely to carry one.
            var marketplaceToolCreateEntry = allEntries.Single(e =>
                e.EntityType == "MarketplaceTool" && e.Action == CommonConsts.AuditActionConsts.Create);
            Assert.NotNull(marketplaceToolCreateEntry.Changes);
            AssertNoTemporaryPlaceholderValues(marketplaceToolCreateEntry.Changes);
        }

        /// <summary>
        /// Regression test for a follow-up gap found while fixing the bug above: when the "reuse existing
        /// marketplace tool" branch's Mapster map produces zero actual value differences (re-sharing a tool
        /// unchanged since its last share), EF Core's change tracker used to leave the entity Unchanged — so
        /// neither MasterDataDbContext.SaveChangesAsync's bookkeeping loop nor AppendAuditLogEntries (whose
        /// ResolveAuditAction returns null for Unchanged) ever ran for it, and a deliberate re-share action
        /// left zero trace. Fixed the same way as PublishTenantToolCommandHandler: explicitly stamp
        /// LastUpdatedOn in that branch to force the entity into the Modified state regardless.
        /// </summary>
        [Fact]
        public async Task ReSharingAnUnchangedTool_StillBumpsLastUpdatedOn_AndWritesAnUpdateAuditEntry()
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantContext = new TenantContext { TenantName = "T1", TenantGuid = "tenant-guid-1" };

            var firstProviderMock = new Mock<IDateTimeProvider>();
            firstProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

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

                var publishedTool = mapper.Map<PublishedTenantTool>(tenantTool);
                publishedTool.TenantId = tenant.Id;
                publishedTool.CreatedByTenantToolId = tenantTool.Id;
                setupDb.PublishedTenantTools.Add(publishedTool);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            // --- First share: creates the MarketplaceTool (the "else" branch) ---
            await using (var firstShareDb = new SqliteTestMasterDataDbContext(options, firstProviderMock.Object))
            {
                firstShareDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-1");
                var handler = new ShareTenantToolCommandHandler(firstShareDb, mapper, tenantContext, firstProviderMock.Object);
                var result = await handler.Handle(new ShareTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolShareResult>>(result);
            }

            // --- Re-share: same TenantTool/PublishedTenantTool, no field changes at all — the regression
            // scenario. Uses a different IDateTimeProvider "now" so LastUpdatedOn's before/after is
            // unambiguous.
            var reShareProviderMock = new Mock<IDateTimeProvider>();
            var reShareNow = Now.AddDays(1);
            reShareProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(reShareNow);

            await using (var reShareDb = new SqliteTestMasterDataDbContext(options, reShareProviderMock.Object))
            {
                reShareDb.SetAuditActor("tenant-guid-1", "user-1", "Alice", "alice@test.com", "corr-2");
                var handler = new ShareTenantToolCommandHandler(reShareDb, mapper, tenantContext, reShareProviderMock.Object);
                var result = await handler.Handle(new ShareTenantToolCommand { Id = tenantToolId }, TestContext.Current.CancellationToken);
                Assert.IsType<SuccessResult<TenantToolShareResult>>(result);
            }

            // --- Assert: the re-share bumped LastUpdatedOn and produced its own Update audit row ---
            await using var verifyDb2 = new SqliteTestMasterDataDbContext(options, reShareProviderMock.Object);
            var marketplaceTool = await verifyDb2.MarketplaceTools.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(reShareNow, marketplaceTool.LastUpdatedOn);

            var reShareAuditEntry = await verifyDb2.AuditLogEntries.SingleAsync(
                a => a.EntityType == "MarketplaceTool" && a.CorrelationId == "corr-2",
                TestContext.Current.CancellationToken);
            Assert.Equal(CommonConsts.AuditActionConsts.Update, reShareAuditEntry.Action);
        }

        /// <summary>
        /// Parses an <see cref="AuditLogEntry.Changes"/> JSON blob and fails if any recorded old/new value
        /// looks like an EF Core temporary key placeholder (a huge negative number near
        /// <see cref="int.MinValue"/> — real application ids/counts/FKs in this codebase never come close).
        /// </summary>
        private static void AssertNoTemporaryPlaceholderValues(string changesJson)
        {
            using var doc = JsonDocument.Parse(changesJson);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                foreach (var side in new[] { "old", "new" })
                {
                    if (property.Value.TryGetProperty(side, out var value) && value.ValueKind == JsonValueKind.Number)
                    {
                        var number = value.GetDouble();
                        Assert.True(
                            number > -1_000_000_000,
                            $"Property '{property.Name}.{side}' looks like an EF Core temporary key placeholder: {number}");
                    }
                }
            }
        }
    }
}
