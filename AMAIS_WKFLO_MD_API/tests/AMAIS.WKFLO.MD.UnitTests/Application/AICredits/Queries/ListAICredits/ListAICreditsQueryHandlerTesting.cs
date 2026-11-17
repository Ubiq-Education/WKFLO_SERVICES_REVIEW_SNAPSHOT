using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AICredits.Queries.ListAICredits;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Queries.ListAICredits
{
    public class ListAICreditsQueryHandlerTesting
    {
        public ListAICreditsQueryHandler ListAICreditsQueryHandler { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int AICreditId { get; private set; } = RandomNumber.Next();

        public ListAICreditsQueryHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var aiCredit = new AICredit()
            {
                Id = AICreditId,
                AIModelId = RandomNumber.Next(),
                TokensIn = 100,
                CostInMTok = 1,
                TokensOut = 100,
                CostOutMTok = 1,
                MultiplierIn = 1,
                MultiplierOut = 1
            };

            var aiCredit2 = new AICredit()
            {
                Id = RandomNumber.Next(),
                AIModelId = RandomNumber.Next(),
                TokensIn = 100,
                CostInMTok = 1,
                TokensOut = 100,
                CostOutMTok = 1,
                MultiplierIn = 1,
                MultiplierOut = 1
            };

            var aiCredits = new List<AICredit>() {
                aiCredit, aiCredit2
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AICredits).Returns(aiCredits.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            ListAICreditsQueryHandler = new ListAICreditsQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
