using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tags.Commands.UnassignTagFromEntity;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Commands.UnassignTagFromEntity
{
    public class UnassignTagFromEntityCommandHandlerTesting
    {
        public UnassignTagFromEntityCommandHandler UnassignTagFromEntityCommandHandler { get; set; }

        public UnassignTagFromEntityCommandHandlerTesting()
        {
            var existingEntityTag = new EntityTag()
            {
                TagId = 1,
                EntityType = "Agent",
                EntityId = 10
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var entityTags = new List<EntityTag>() { existingEntityTag }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.EntityTags).Returns(entityTags.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UnassignTagFromEntityCommandHandler = new UnassignTagFromEntityCommandHandler(db.Object, mapper);
        }
    }
}
