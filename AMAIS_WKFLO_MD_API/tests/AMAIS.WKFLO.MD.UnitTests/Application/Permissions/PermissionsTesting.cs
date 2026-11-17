using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Permissions
{
    /// <summary>
    /// Shared fixture for the super power user / tenant access handler tests. Builds a mocked
    /// <see cref="IMasterDataDbContext"/> over the given rows; call <see cref="CreateScenario"/> per test
    /// because the handlers mutate the seeded entities.
    /// </summary>
    public class PermissionsTesting
    {
        public const string CallerEmail = "admin@example.com";

        public static readonly Guid TenantAGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid TenantBGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");

        public sealed class Scenario
        {
            public Mock<IMasterDataDbContext> Db { get; init; }
            public UserContext User { get; init; }
            public List<SuperPowerUser> SuperPowerUsers { get; init; }
            public List<TenantAccess> TenantAccesses { get; init; }
            public List<Tenant> Tenants { get; init; }
            public List<object> Added { get; } = new();
        }

        public static List<Tenant> DefaultTenants() => new()
        {
            new() { Id = 1, Guid = TenantAGuid, Name = "tenant-a" },
            new() { Id = 2, Guid = TenantBGuid, Name = "tenant-b" }
        };

        public Scenario CreateScenario(
            string callerEmail,
            List<SuperPowerUser> superPowerUsers = null,
            List<TenantAccess> tenantAccesses = null,
            List<Tenant> tenants = null)
        {
            superPowerUsers ??= new List<SuperPowerUser>();
            tenantAccesses ??= new List<TenantAccess>();
            tenants ??= DefaultTenants();

            foreach (var access in tenantAccesses)
                access.Tenant ??= tenants.Find(t => t.Id == access.TenantId);

            var superDbSet = superPowerUsers.BuildMockDbSet();
            var accessDbSet = tenantAccesses.BuildMockDbSet();
            var tenantDbSet = tenants.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.SuperPowerUsers).Returns(superDbSet.Object);
            db.Setup(x => x.TenantAccesses).Returns(accessDbSet.Object);
            db.Setup(x => x.Tenants).Returns(tenantDbSet.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var scenario = new Scenario
            {
                Db = db,
                User = new UserContext { Email = callerEmail },
                SuperPowerUsers = superPowerUsers,
                TenantAccesses = tenantAccesses,
                Tenants = tenants
            };

            superDbSet.Setup(x => x.Add(It.IsAny<SuperPowerUser>()))
                .Callback<SuperPowerUser>(e => { superPowerUsers.Add(e); scenario.Added.Add(e); });
            accessDbSet.Setup(x => x.Add(It.IsAny<TenantAccess>()))
                .Callback<TenantAccess>(e => { tenantAccesses.Add(e); scenario.Added.Add(e); });

            return scenario;
        }
    }
}
