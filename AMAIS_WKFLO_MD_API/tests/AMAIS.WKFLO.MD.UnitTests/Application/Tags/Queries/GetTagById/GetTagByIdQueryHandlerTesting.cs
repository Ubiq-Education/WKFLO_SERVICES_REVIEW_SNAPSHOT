using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tags.Queries.GetTagById;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Queries.GetTagById
{
    public class GetTagByIdQueryHandlerTesting
    {
        public GetTagByIdQueryHandler GetTagByIdQueryHandler { get; set; }

        public GetTagByIdQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant() { Id = 1, Name = tenantContext.TenantName };

            var existingTag = new Tag()
            {
                Id = 1,
                Name = "my-tag",
                Color = "#FF0000",
                TenantId = 1,
                Tenant = tenant
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var tags = new List<Tag>() { existingTag }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tags).Returns(tags.Object);

            GetTagByIdQueryHandler = new GetTagByIdQueryHandler(db.Object, mapper, tenantContext);
        }
    }
}
