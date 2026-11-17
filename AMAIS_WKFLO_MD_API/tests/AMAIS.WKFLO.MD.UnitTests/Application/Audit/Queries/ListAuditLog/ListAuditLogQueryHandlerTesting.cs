using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.MD.Application.Audit.Queries.ListAuditLog;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Audit.Queries.ListAuditLog
{
    public class ListAuditLogQueryHandlerTesting
    {
        public ListAuditLogQueryHandler ListAuditLogQueryHandler { get; set; }

        public const string TenantGuid = "11111111-1111-1111-1111-111111111111";
        public const string OtherTenantGuid = "22222222-2222-2222-2222-222222222222";

        public ListAuditLogQueryHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = "Test", TenantGuid = TenantGuid };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

            var auditLogEntries = new List<AuditLogEntry>
            {
                new AuditLogEntry { Id = 1, TenantGuid = TenantGuid, EntityType = "TenantAgent", EntityId = "1", Action = CommonConsts.AuditActionConsts.Create, ActorUserGuid = "10", ActorUserName = "Alice", ActorUserEmail = "alice@test.com", Timestamp = fixedDate.AddMinutes(-1) },
                new AuditLogEntry { Id = 2, TenantGuid = TenantGuid, EntityType = "TenantAgent", EntityId = "1", Action = CommonConsts.AuditActionConsts.Update, ActorUserGuid = "10", ActorUserName = "Alice", ActorUserEmail = "alice@test.com", Timestamp = fixedDate.AddMinutes(-2), Changes = "{\"Name\":{\"old\":\"A\",\"new\":\"B\"}}" },
                new AuditLogEntry { Id = 3, TenantGuid = TenantGuid, EntityType = "Secret", EntityId = "5", Action = CommonConsts.AuditActionConsts.Delete, ActorUserGuid = "20", ActorUserName = "Bob", ActorUserEmail = "bob@test.com", Timestamp = fixedDate.AddMinutes(-3) },
                new AuditLogEntry { Id = 4, TenantGuid = TenantGuid, EntityType = "Tag", EntityId = "7", Action = CommonConsts.AuditActionConsts.Create, ActorUserGuid = "20", ActorUserName = "Bob", ActorUserEmail = "bob@test.com", Timestamp = fixedDate.AddMinutes(-4) },
                new AuditLogEntry { Id = 5, TenantGuid = TenantGuid, EntityType = "Tag", EntityId = "8", Action = CommonConsts.AuditActionConsts.Create, ActorUserGuid = "20", ActorUserName = "Bob", ActorUserEmail = "bob@test.com", Timestamp = fixedDate.AddMinutes(-5) },
                // Belongs to a different tenant — must never show up regardless of filters.
                new AuditLogEntry { Id = 6, TenantGuid = OtherTenantGuid, EntityType = "TenantAgent", EntityId = "99", Action = CommonConsts.AuditActionConsts.Create, ActorUserGuid = "30", ActorUserName = "Eve", ActorUserEmail = "eve@other.com", Timestamp = fixedDate.AddMinutes(-1) },
                // Late in the same calendar day as the others (fixedDate is 2026-01-01T12:00:00Z) — used to
                // prove DateTo is inclusive of the whole day it names, not just up to that exact instant.
                new AuditLogEntry { Id = 7, TenantGuid = TenantGuid, EntityType = "Secret", EntityId = "9", Action = CommonConsts.AuditActionConsts.Create, ActorUserGuid = "40", ActorUserName = "Carol", ActorUserEmail = "carol@test.com", Timestamp = new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero) },
            }.BuildMockDbSet();

            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AuditLogEntries).Returns(auditLogEntries.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);

            ListAuditLogQueryHandler = new ListAuditLogQueryHandler(
                db.Object,
                mapper,
                tenantContext
            );
        }
    }
}
