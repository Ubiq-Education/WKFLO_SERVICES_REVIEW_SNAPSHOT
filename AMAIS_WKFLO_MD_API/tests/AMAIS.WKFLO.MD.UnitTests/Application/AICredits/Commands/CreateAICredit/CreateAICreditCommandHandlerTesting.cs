using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.CreateAICredit;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.CreateAICredit
{
    public class CreateAICreditCommandHandlerTesting
    {
        public CreateAICreditCommandHandler CreateAICreditCommandHandler { get; set; }

        public CreateAICreditCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var aiCredits = new List<AICredit>() { }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AICredits).Returns(aiCredits.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            CreateAICreditCommandHandler = new CreateAICreditCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
