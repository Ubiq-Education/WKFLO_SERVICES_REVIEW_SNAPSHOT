using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.UpdateAICredit;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.UpdateAICredit
{
    public class UpdateAICreditCommandHandlerTests : IClassFixture<UpdateAICreditCommandHandlerTesting>
    {
        private readonly UpdateAICreditCommandHandlerTesting _testing;

        public UpdateAICreditCommandHandlerTests(UpdateAICreditCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.UpdateAICreditCommandHandler.Handle(new UpdateAICreditCommand()
            {
                Id = _testing.AICreditId,
                AIModelId = _testing.AIModelId,
                TokensIn = 500,
                CostInMTok = 2,
                TokensOut = 500,
                CostOutMTok = 2,
                MultiplierIn = 1.5m,
                MultiplierOut = 1.5m
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<AICreditUpdateResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(500, success.Data.TokensIn);
                Assert.Equal(1.5m, success.Data.MultiplierIn);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.UpdateAICreditCommandHandler.Handle(new UpdateAICreditCommand()
            {
                Id = 0,
                AIModelId = _testing.AIModelId,
                TokensIn = 500,
                CostInMTok = 2,
                TokensOut = 500,
                CostOutMTok = 2,
                MultiplierIn = 1.5m,
                MultiplierOut = 1.5m
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
