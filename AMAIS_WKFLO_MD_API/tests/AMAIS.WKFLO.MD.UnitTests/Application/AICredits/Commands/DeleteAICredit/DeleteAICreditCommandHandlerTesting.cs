using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.DeleteAICredit;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.DeleteAICredit
{
    public class DeleteAICreditCommandHandlerTesting
    {
        public DeleteAICreditCommandHandler DeleteAICreditCommandHandler { get; set; }
        public int AICreditId { get; set; } = 1;

        public DeleteAICreditCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var aiCredit = new AICredit()
            {
                Id = AICreditId,
                AIModelId = 1,
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

            DeleteAICreditCommandHandler = new DeleteAICreditCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
