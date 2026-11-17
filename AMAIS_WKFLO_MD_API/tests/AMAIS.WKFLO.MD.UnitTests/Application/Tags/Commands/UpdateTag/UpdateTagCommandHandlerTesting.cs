using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tags.Commands.UpdateTag;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Commands.UpdateTag
{
    public class UpdateTagCommandHandlerTesting
    {
        public UpdateTagCommandHandler UpdateTagCommandHandler { get; set; }

        public UpdateTagCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant() { Id = 1, Name = tenantContext.TenantName };

            var existingTag = new Tag()
            {
                Id = 1,
                Name = "original",
                Color = "#000000",
                TenantId = 1,
                Tenant = tenant
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var tags = new List<Tag>() { existingTag }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tags).Returns(tags.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UpdateTagCommandHandler = new UpdateTagCommandHandler(db.Object, mapper);
        }
    }
}
