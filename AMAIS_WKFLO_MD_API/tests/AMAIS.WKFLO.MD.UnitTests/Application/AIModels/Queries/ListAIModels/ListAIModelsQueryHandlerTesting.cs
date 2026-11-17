using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AIModels.Queries.ListAIModels;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Queries.ListAIModels
{
    public class ListAIModelsQueryHandlerTesting
    {
        public ListAIModelsQueryHandler ListAIModelsQueryHandler { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int AIModelId { get; private set; } = RandomNumber.Next();

        public ListAIModelsQueryHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var aimodel = new AIModel()
            {
                Id = AIModelId,
                Name = "Test Name",
                Model = "Test Model",
                Provider = "Test Provider",
                Type = "Test Type",
                IsActive = true
            };

            var aimodel2 = new AIModel()
            {
                Id = RandomNumber.Next(),
                Name = "Test Name",
                Model = "Test Model",
                Provider = "Test Provider",
                Type = "Test Type",
                IsActive = true
            };

            var aimodels = new List<AIModel>() {
                aimodel, aimodel2
            }.BuildMockDbSet();

            var entityTags = new List<EntityTag>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AIModels).Returns(aimodels.Object);
            db.Setup(x => x.EntityTags).Returns(entityTags.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            ListAIModelsQueryHandler = new ListAIModelsQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
