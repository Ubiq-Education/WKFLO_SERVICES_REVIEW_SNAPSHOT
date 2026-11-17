using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tags.Queries.ListTenantTags;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Queries.ListTenantTags
{
    public class ListTenantTagsQueryHandlerTesting
    {
        public ListTenantTagsQueryHandler ListTenantTagsQueryHandler { get; set; }

        public ListTenantTagsQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant() { Id = 1, Name = tenantContext.TenantName };

            var tag1 = new Tag() { Id = 1, Name = "frontend", Color = "#FF0000", TenantId = 1, Tenant = tenant };
            var tag2 = new Tag() { Id = 2, Name = "backend", Color = "#00FF00", TenantId = 1, Tenant = tenant };
            var tag3 = new Tag() { Id = 3, Name = "urgent", TenantId = 1, Tenant = tenant };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var tags = new List<Tag>() { tag1, tag2, tag3 }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tags).Returns(tags.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);

            ListTenantTagsQueryHandler = new ListTenantTagsQueryHandler(db.Object, mapper, tenantContext);
        }
    }
}
