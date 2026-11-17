using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetTenantToolById;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Queries.GetTenantToolById
{
    public class GetTenantToolByIdQueryHandlerTesting
    {
        public GetTenantToolByIdQueryHandler GetTenantToolByIdQueryHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int TenantToolId { get; private set; } = RandomNumber.Next();
        public Guid McpOAuthConnectionId { get; } = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        public GetTenantToolByIdQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant()
            {
                Id = 1,
                Name = tenantContext.TenantName
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tenantTool = new TenantTool()
            {
                Id = TenantToolId,
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "{}",
                McpOAuthConnectionId = McpOAuthConnectionId,
                IsActive = true,
                CreatedOn = fixedDate,
                Tenant = tenant,
            };

            var tenantTool2 = new TenantTool()
            {
                Id = RandomNumber.Next(),
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type",
                Config = "{}",
                IsActive = true,
                CreatedOn = fixedDate,
                Tenant = tenant,
            };

            var tenantTools = new List<TenantTool>() {
                tenantTool, tenantTool2
            }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var entityTags = new List<EntityTag>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.EntityTags).Returns(entityTags.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            GetTenantToolByIdQueryHandler = new GetTenantToolByIdQueryHandler
            (
                db.Object,
                mapper,
                tenantContext
            );
        }
    }
}

