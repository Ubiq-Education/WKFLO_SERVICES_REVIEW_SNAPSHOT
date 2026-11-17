using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tags.Commands.CreateTag;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Commands.CreateTag
{
    public class CreateTagCommandHandlerTesting
    {
        public CreateTagCommandHandler CreateTagCommandHandler { get; set; }

        public CreateTagCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant() { Id = 1, Name = tenantContext.TenantName };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var tags = new List<Tag>() { }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tags).Returns(tags.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            CreateTagCommandHandler = new CreateTagCommandHandler(db.Object, mapper, tenantContext);
        }
    }
}
