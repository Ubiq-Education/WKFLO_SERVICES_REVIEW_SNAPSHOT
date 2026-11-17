using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.AddSuperPowerUser;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.RemoveSuperPowerUser;
using AMAIS.WKFLO.MD.Application.SuperPowerUsers.Models;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.AddTenantAccess;
using AMAIS.WKFLO.MD.Application.TenantAccesses.Models;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.MD.UnitTests.Application.Permissions.PermissionsTesting;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Permissions
{
    /// <summary>
    /// The duplicate-key retry path and the post-save revert can only be exercised against a real unique index
    /// and change tracker, so these run the handlers on in-memory SQLite and simulate the concurrent request
    /// from a second context (or a callback) just before the handler's save hits the database.
    /// </summary>
    public class PermissionRaceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static Mock<IDateTimeProvider> Clock()
        {
            var clock = new Mock<IDateTimeProvider>();
            clock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);
            return clock;
        }

        private static SqliteTestMasterDataDbContext NewContext(SqliteConnection connection, IDateTimeProvider clock, params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).AddInterceptors(interceptors).Options;
            var db = new SqliteTestMasterDataDbContext(options, clock);
            db.SetAuditActor("tenant-guid", "user-guid", "User", CallerEmail, "corr-1");
            return db;
        }

        [Fact]
        public async Task AddSuperPowerUser_ConcurrentInsertOfSameEmail_RetriesAndReturnsTheCompetingRow()
        {
            var ct = TestContext.Current.CancellationToken;
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(ct);
            var clock = Clock();

            await using (var seed = NewContext(connection, clock.Object))
            {
                await seed.Database.EnsureCreatedAsync(ct);
                seed.SuperPowerUsers.Add(new SuperPowerUser { Email = CallerEmail });
                await seed.SaveChangesAsync(ct);
            }

            var competitor = new CompetingInsertInterceptor<SuperPowerUser>(async token =>
            {
                await using var other = NewContext(connection, clock.Object);
                other.SuperPowerUsers.Add(new SuperPowerUser { Email = "new@example.com", CreatedBy = "other.admin@example.com" });
                await other.SaveChangesAsync(token);
            });

            await using var db = NewContext(connection, clock.Object, competitor);
            var handler = new AddSuperPowerUserCommandHandler(db, new UserContext { Email = CallerEmail });

            var result = await handler.Handle(new AddSuperPowerUserCommand { Email = "New@Example.com" }, ct);

            var data = Assert.IsType<SuccessResult<SuperPowerUserResult>>(result).Data;
            Assert.Equal("new@example.com", data.Email);
            Assert.Equal("other.admin@example.com", data.CreatedBy);
            Assert.Equal(1, competitor.Calls);

            await using var verify = NewContext(connection, clock.Object);
            Assert.Single(await verify.SuperPowerUsers.AsNoTracking().Where(s => s.Email == "new@example.com").ToListAsync(ct));
        }

        [Fact]
        public async Task AddTenantAccess_ConcurrentInsertOfSameGrant_RetriesAndReturnsTheCompetingRow()
        {
            var ct = TestContext.Current.CancellationToken;
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(ct);
            var clock = Clock();

            int tenantId;
            await using (var seed = NewContext(connection, clock.Object))
            {
                await seed.Database.EnsureCreatedAsync(ct);
                var tenant = new Tenant { Name = "tenant-a", Guid = TenantAGuid };
                seed.Tenants.Add(tenant);
                seed.SuperPowerUsers.Add(new SuperPowerUser { Email = CallerEmail });
                await seed.SaveChangesAsync(ct);
                tenantId = tenant.Id;
            }

            var competitor = new CompetingInsertInterceptor<TenantAccess>(async token =>
            {
                await using var other = NewContext(connection, clock.Object);
                other.TenantAccesses.Add(new TenantAccess { TenantId = tenantId, Email = "user@example.com", CreatedBy = "other.admin@example.com" });
                await other.SaveChangesAsync(token);
            });

            await using var db = NewContext(connection, clock.Object, competitor);
            var handler = new AddTenantAccessCommandHandler(db, new UserContext { Email = CallerEmail });

            var result = await handler.Handle(new AddTenantAccessCommand { TenantGuid = TenantAGuid, Email = "User@Example.com" }, ct);

            var data = Assert.IsType<SuccessResult<TenantAccessResult>>(result).Data;
            Assert.Equal("user@example.com", data.Email);
            Assert.Equal("other.admin@example.com", data.CreatedBy);
            Assert.Equal(1, competitor.Calls);

            await using var verify = NewContext(connection, clock.Object);
            Assert.Single(await verify.TenantAccesses.AsNoTracking().Where(a => a.TenantId == tenantId).ToListAsync(ct));
        }

        [Fact]
        public async Task RemoveSuperPowerUser_ConcurrentMutualRemoval_RevertsSoOneActiveRemains()
        {
            var ct = TestContext.Current.CancellationToken;
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(ct);
            var clock = Clock();

            int callerRowId;
            int otherRowId;
            await using (var seed = NewContext(connection, clock.Object))
            {
                await seed.Database.EnsureCreatedAsync(ct);
                var caller = new SuperPowerUser { Email = CallerEmail };
                var other = new SuperPowerUser { Email = "other@example.com" };
                seed.SuperPowerUsers.AddRange(caller, other);
                await seed.SaveChangesAsync(ct);
                callerRowId = caller.Id;
                otherRowId = other.Id;
            }

            // The other admin removes the caller's row right after our handler's pre-check, before its save commits.
            var competitor = new CompetingUpdateInterceptor(async token =>
            {
                await using var concurrent = NewContext(connection, clock.Object);
                var row = await concurrent.SuperPowerUsers.AsTracking().SingleAsync(s => s.Id == callerRowId, token);
                row.IsDeleted = true;
                await concurrent.SaveChangesAsync(token);
            });

            await using var db = NewContext(connection, clock.Object, competitor);
            var handler = new RemoveSuperPowerUserCommandHandler(db, new UserContext { Email = CallerEmail });

            var result = await handler.Handle(new RemoveSuperPowerUserCommand { Id = otherRowId }, ct);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Equal(ErrorConst.CannotRemoveLastSuperPowerUser, Assert.Single(invalid.Errors).Code);

            await using var verify = NewContext(connection, clock.Object);
            var active = await verify.SuperPowerUsers.AsNoTracking().Where(s => !s.IsDeleted).ToListAsync(ct);
            var remaining = Assert.Single(active);
            Assert.Equal(otherRowId, remaining.Id);
        }

        private sealed class CompetingInsertInterceptor<TEntity> : SaveChangesInterceptor where TEntity : class
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
                // Only the handler's first save, and only when it is about to insert the entity.
                if (Calls == 0 && eventData.Context!.ChangeTracker.Entries<TEntity>().Any(e => e.State == EntityState.Added))
                {
                    Calls++;
                    await _competingInsert(cancellationToken);
                }

                return result;
            }
        }

        private sealed class CompetingUpdateInterceptor : SaveChangesInterceptor
        {
            private readonly Func<CancellationToken, Task> _competingUpdate;
            private bool _fired;

            public CompetingUpdateInterceptor(Func<CancellationToken, Task> competingUpdate)
            {
                _competingUpdate = competingUpdate;
            }

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                // Only the handler's first save (the soft delete), not the revert.
                if (!_fired && eventData.Context!.ChangeTracker.Entries<SuperPowerUser>().Any(e => e.State == EntityState.Modified))
                {
                    _fired = true;
                    await _competingUpdate(cancellationToken);
                }

                return result;
            }
        }
    }
}
