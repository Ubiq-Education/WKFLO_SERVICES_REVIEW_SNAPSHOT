using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.CreateAICredit;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.CreateAICredit
{
    public class CreateAICreditCommandHandlerTests : IClassFixture<CreateAICreditCommandHandlerTesting>
    {
        private readonly CreateAICreditCommandHandlerTesting _testing;

        public CreateAICreditCommandHandlerTests(CreateAICreditCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, 1000L, 1.5, 2000L, 3.0, 1.0, 1.0, ResultType.Ok)]
        [InlineData(2, 500L, 0.5, 500L, 0.5, 2.5, 1.25, ResultType.Ok)]
        public async Task TestHandler(int aiModelId, long tokensIn, decimal costInMTok, long tokensOut, decimal costOutMTok, decimal multiplierIn, decimal multiplierOut, ResultType resultType)
        {
            // Act
            var result = await _testing.CreateAICreditCommandHandler.Handle(new CreateAICreditCommand()
            {
                AIModelId = aiModelId,
                TokensIn = tokensIn,
                CostInMTok = costInMTok,
                TokensOut = tokensOut,
                CostOutMTok = costOutMTok,
                MultiplierIn = multiplierIn,
                MultiplierOut = multiplierOut
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<AICreditCreateResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(aiModelId, success.Data.AIModelId);
                Assert.Equal(tokensIn, success.Data.TokensIn);
                Assert.Equal(tokensOut, success.Data.TokensOut);
                Assert.Equal(multiplierIn, success.Data.MultiplierIn);
                Assert.Equal(multiplierOut, success.Data.MultiplierOut);
            }
        }
    }
}
