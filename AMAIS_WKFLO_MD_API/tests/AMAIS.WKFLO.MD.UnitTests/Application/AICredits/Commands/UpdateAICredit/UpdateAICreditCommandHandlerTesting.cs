using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.UpdateAICredit;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.UpdateAICredit
{
    public class UpdateAICreditCommandHandlerTesting
    {
        public UpdateAICreditCommandHandler UpdateAICreditCommandHandler { get; set; }
        public int AICreditId { get; set; } = 1;
        public int AIModelId { get; set; } = 1;

        public UpdateAICreditCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var aiCredit = new AICredit()
            {
                Id = AICreditId,
                AIModelId = AIModelId,
                TokensIn = 100,
                CostInMTok = 1,
                TokensOut = 100,
                CostOutMTok = 1,
                MultiplierIn = 1,
                MultiplierOut = 1
            };

            var aiCredits = new List<AICredit>() { aiCredit }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AICredits).Returns(aiCredits.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UpdateAICreditCommandHandler = new UpdateAICreditCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
