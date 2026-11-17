using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.WebOrigins.Queries.ListTenantWebOrigins;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.WebOrigins.Queries.ListTenantWebOrigins
{
    public class ListTenantWebOriginsQueryHandlerTesting
    {
        public const string CallerEmail = "admin@example.com";
        public const string TenantName = "tenant-a";

        public ListTenantWebOriginsQueryHandler CreateHandler(
            string callerEmail,
            string tenantName,
            List<SuperPowerUser> superPowerUsers,
            List<Tenant> tenants,
            List<WebOrigin> webOrigins)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.SuperPowerUsers).Returns(superPowerUsers.BuildMockDbSet().Object);
            db.Setup(x => x.Tenants).Returns(tenants.BuildMockDbSet().Object);
            db.Setup(x => x.WebOrigins).Returns(webOrigins.BuildMockDbSet().Object);

            return new ListTenantWebOriginsQueryHandler(
                db.Object,
                new TenantContext { TenantName = tenantName },
                new UserContext { Email = callerEmail });
        }

        public static List<SuperPowerUser> Admins() => new() { new() { Id = 1, Email = CallerEmail } };

        public static List<Tenant> TenantRows() => new()
        {
            new() { Id = 1, Name = TenantName },
            new() { Id = 2, Name = "tenant-b" }
        };

        public static WebOrigin Origin(int id, int tenantId, string type, string name, bool isActive = true,
            bool isDeleted = false, DateTimeOffset? createdOn = null, DateTimeOffset? updatedOn = null) => new()
        {
            Id = id,
            TenantId = tenantId,
            WebOriginType = type,
            Name = name,
            IsActive = isActive,
            IsDeleted = isDeleted,
            CreatedOn = createdOn ?? DateTimeOffset.UnixEpoch,
            LastUpdatedOn = updatedOn
        };
    }
}
