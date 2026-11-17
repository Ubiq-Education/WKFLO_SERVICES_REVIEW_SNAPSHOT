using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.UpdateAIModel;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Commands.UpdateAIModel
{
    public class UpdateAIModelCommandHandlerTesting
    {
        public UpdateAIModelCommandHandler UpdateAIModelCommandHandler { get; set; }
        public int AIModelId { get; set; } = 1;

        public UpdateAIModelCommandHandlerTesting()
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

            var aimodels = new List<AIModel>() { aimodel }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AIModels).Returns(aimodels.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UpdateAIModelCommandHandler = new UpdateAIModelCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
