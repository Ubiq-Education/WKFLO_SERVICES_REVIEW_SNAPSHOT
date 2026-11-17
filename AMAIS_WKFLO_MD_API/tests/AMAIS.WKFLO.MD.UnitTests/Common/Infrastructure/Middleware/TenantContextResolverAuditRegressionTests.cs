using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Audit.Models;
using AMAIS.WKFLO.MD.Application.Audit.Queries.ListAuditLog;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.Infrastructure.Middleware
{
    /// <summary>
    /// Regression tests for a bug found by GitHub Copilot's automated review on PR !16299 (confirmed against
    /// the real code, not a false positive): a TenantName-only request — no TenantGuid header, the documented
    /// backward-compatibility path called out in <see cref="TenantContextResolver"/>'s own comment ("WKFLO UI
    /// should use TenantGuid to identify tenant, and TenantName is only for backward compatibility") — left
    /// <see cref="TenantContext.TenantGuid"/> permanently blank. That silently broke the audit trail on both
    /// sides at once: <c>MasterDataDbContext.AppendAuditLogEntries</c> skips entirely whenever TenantGuid is
    /// blank (write side — <see cref="AuditActorContextResolver"/> just forwards whatever
    /// <see cref="TenantContext.TenantGuid"/> holds), and <see cref="ListAuditLogQueryHandler"/> filters
    /// strictly by <see cref="TenantContext.TenantGuid"/> (read side) — so even a tenant with real,
    /// GUID-audited rows already in the table would see an empty audit log if its session happened to resolve
    /// via TenantName instead.
    /// </summary>
    /// <remarks>
    /// Fixed by having <see cref="TenantContextResolver"/> resolve the canonical TenantGuid from the Tenant
    /// looked up by name, mirroring the reverse (GUID -> canonical Name) resolution it already performed —
    /// so both the write side and the read side get a real GUID for free, without duplicating tenant-lookup
    /// logic in either of those two places.
    ///
    /// These tests exercise the real <see cref="TenantContextResolver"/> middleware (real
    /// <see cref="DefaultHttpContext"/>, real SQLite-backed <see cref="MasterDataDbContext"/>) end to end
    /// through to a real write (<c>SaveChangesAsync</c>) and a real read (<see cref="ListAuditLogQueryHandler"/>),
    /// since the bug is specifically about whether TenantGuid actually flows from the header all the way to
    /// both those places — a test that stubs any one of those pieces out could hide the very gap being fixed.
    /// </remarks>
    public class TenantContextResolverAuditRegressionTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static async Task<(SqliteConnection Connection, DbContextOptions<MasterDataDbContext> Options)> CreateSchemaAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;

            await using (var schemaContext = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>()))
                await schemaContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            return (connection, options);
        }

        private static HttpContext BuildHttpContext(string tenantNameHeader, IMasterDataDbContext db, TenantContext tenantContext)
        {
            var services = new ServiceCollection();
            services.AddSingleton(tenantContext);
            services.AddSingleton(db);
            var provider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = provider };
            if (tenantNameHeader != null)
            {
                httpContext.Request.Headers[CommonConsts.TenantName] = tenantNameHeader;
            }

            return httpContext;
        }

        [Fact]
        public async Task TenantNameOnlyRequest_ResolvesTenantGuid_AndAuditRow_IsWrittenAndReadableBack()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);
            var dateTimeProvider = dateTimeProviderMock.Object;

            Guid tenantGuid;
            await using (var setupDb = new SqliteTestMasterDataDbContext(options, dateTimeProvider))
            {
                var tenant = new Tenant { Name = "Acme", Guid = Guid.NewGuid() };
                setupDb.Tenants.Add(tenant);
                await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantGuid = tenant.Guid;
            }

            // --- Act: run TenantContextResolver with only the TenantName header set (no TenantGuid header) ---
            await using var db = new SqliteTestMasterDataDbContext(options, dateTimeProvider);
            var tenantContext = new TenantContext();
            var httpContext = BuildHttpContext("Acme", db, tenantContext);

            var nextCalled = false;
            var resolver = new TenantContextResolver(_ => { nextCalled = true; return Task.CompletedTask; });
            await resolver.Invoke(httpContext);

            Assert.True(nextCalled);
            Assert.Equal("Acme", tenantContext.TenantName);
            Assert.Equal(tenantGuid.ToString(), tenantContext.TenantGuid);

            // --- Simulate AuditActorContextResolver, which runs right after TenantContextResolver in the
            // real pipeline and just forwards whatever TenantGuid it finds on TenantContext, then perform a
            // real write through the same db instance ---
            db.SetAuditActor(tenantContext.TenantGuid, "user-1", "Alice", "alice@test.com", "corr-1");
            db.Secrets.Add(new Secret { Name = "s1", IsActive = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(tenantGuid.ToString(), entry.TenantGuid);

            // --- Read side: ListAuditLogQueryHandler filters strictly by TenantContext.TenantGuid, so the
            // same resolved value must round-trip back to the same session's audit list ---
            var mapper = WkfloMapperExtensions.CreateMapper();
            var handler = new ListAuditLogQueryHandler(db, mapper, tenantContext);
            var result = await handler.Handle(new ListAuditLogQuery(), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(1, success.Data.TotalCount);
            Assert.Equal("Secret", success.Data.Items.Single().EntityType);
        }

        [Fact]
        public async Task TenantNameOnlyRequest_UnknownTenantName_LeavesTenantGuidBlank_LikeToday()
        {
            var (connection, options) = await CreateSchemaAsync();
            await using var _ = connection;

            await using var db = new SqliteTestMasterDataDbContext(options, Mock.Of<IDateTimeProvider>());

            var tenantContext = new TenantContext();
            var httpContext = BuildHttpContext("DoesNotExist", db, tenantContext);

            var resolver = new TenantContextResolver(_ => Task.CompletedTask);
            await resolver.Invoke(httpContext);

            // TenantName is still recorded (unchanged, existing behavior), but since it doesn't resolve to a
            // real tenant, TenantGuid must stay unset — exactly like before this fix, so unrelated flows that
            // already tolerate an unresolved TenantName aren't newly broken by this change.
            Assert.Equal("DoesNotExist", tenantContext.TenantName);
            Assert.True(string.IsNullOrEmpty(tenantContext.TenantGuid));
        }
    }
}
