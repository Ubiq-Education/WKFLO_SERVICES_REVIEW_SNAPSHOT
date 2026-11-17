using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Commands.CreateAIModel
{
    public class CreateAIModelCommandHandlerTesting
    {
        public CreateAIModelCommandHandler CreateAIModelCommandHandler { get; set; }

        public CreateAIModelCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var aimodels = new List<AIModel>() { }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AIModels).Returns(aimodels.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            CreateAIModelCommandHandler = new CreateAIModelCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
