using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.TenantSettings.Commands.UpdateTenantSettings;
using AMAIS.WKFLO.MD.Application.TenantSettings.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantSettings.Commands.UpdateTenantSettings
{
    /// <summary>
    /// The duplicate-key retry path can't be exercised through a mocked <c>IMasterDataDbContext</c> (it
    /// depends on a real unique index and a real change tracker), so these tests run the handler against
    /// a real <see cref="MasterDataDbContext"/> on in-memory SQLite and simulate a concurrent request by
    /// inserting the competing row from a second context just before the handler's save hits the database.
    /// </summary>
    public class UpdateTenantSettingsCommandHandlerConcurrencyTests
    {
        private const string TenantName = "test-tenant";

        [Fact]
        public async Task Handle_ConcurrentFirstTimeInsertForSameTenant_RetriesAndUpdatesTheCompetingRow()
        {
            var ct = TestContext.Current.CancellationToken;
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(ct);

            var plainOptions = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(x => x.DateTimeOffsetUtcNow()).Returns(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            int tenantId;
            await using (var seed = new SqliteTestMasterDataDbContext(plainOptions, dateTimeProvider.Object))
            {
                await seed.Database.EnsureCreatedAsync(ct);
                var tenant = new Tenant { Name = TenantName, Guid = Guid.NewGuid() };
                seed.Tenants.Add(tenant);
                await seed.SaveChangesAsync(ct);
                tenantId = tenant.Id;
            }

            // Fires once, after the handler has read "no settings row" but before its INSERT runs.
            var competitor = new CompetingInsertInterceptor(async token =>
            {
                await using var other = new SqliteTestMasterDataDbContext(plainOptions, dateTimeProvider.Object);
                other.TenantSettings.Add(new TenantSetting { TenantId = tenantId, TimeZone = "Asia/Tokyo" });
                await other.SaveChangesAsync(token);
            });

            var handlerOptions = new DbContextOptionsBuilder<MasterDataDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(competitor)
                .Options;

            await using var db = new SqliteTestMasterDataDbContext(handlerOptions, dateTimeProvider.Object);
            db.SetAuditActor("tenant-guid", "user-guid", "User", "user@example.com", "corr-1");
            var handler = new UpdateTenantSettingsCommandHandler(db, new TenantContext { TenantName = TenantName });

            var result = await handler.Handle(
                new UpdateTenantSettingsCommand { TimeZone = "Europe/London", TimeFormat = "12h" },
                ct);

            var success = Assert.IsType<SuccessResult<TenantSettingsResult>>(result);
            Assert.Equal("Europe/London", success.Data.TimeZone);
            Assert.Equal("12h", success.Data.TimeFormat);
            Assert.Equal(1, competitor.Calls);

            await using var verify = new SqliteTestMasterDataDbContext(plainOptions, dateTimeProvider.Object);
            var row = Assert.Single(await verify.TenantSettings.AsNoTracking().Where(s => s.TenantId == tenantId).ToListAsync(ct));
            Assert.Equal("Europe/London", row.TimeZone);
            Assert.Equal("12h", row.TimeFormat);

            // The failed first save's audit rows must have been discarded with the rest of the tracker:
            // only the successful retry's change (an update of the competing row) is audited.
            var audit = await verify.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(TenantSetting)).ToListAsync(ct);
            var update = Assert.Single(audit);
            Assert.Equal(CommonConsts.AuditActionConsts.Update, update.Action);
            Assert.Equal(row.Id.ToString(), update.EntityId);
        }

        private sealed class CompetingInsertInterceptor : SaveChangesInterceptor
        {
            private readonly Func<CancellationToken, Task> _competingInsert;

            public CompetingInsertInterceptor(Func<CancellationToken, Task> competingInsert)
            {
                _competingInsert = competingInsert;
            }

            public int Calls { get; private set; }

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                // Only the handler's first save, and only when it is about to insert a TenantSetting.
                if (Calls == 0 && eventData.Context!.ChangeTracker.Entries<TenantSetting>().Any(e => e.State == EntityState.Added))
                {
                    Calls++;
                    await _competingInsert(cancellationToken);
                }

                return result;
            }
        }
    }
}
