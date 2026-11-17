using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditByAIModelId;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Queries.GetAICreditByAIModelId
{
    public class GetAICreditByAIModelIdQueryHandlerTests : IClassFixture<GetAICreditByAIModelIdQueryHandlerTesting>
    {
        private readonly GetAICreditByAIModelIdQueryHandlerTesting _testing;

        public GetAICreditByAIModelIdQueryHandlerTests(GetAICreditByAIModelIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.GetAICreditByAIModelIdQueryHandler.Handle(new GetAICreditByAIModelIdQuery()
            {
                AIModelId = _testing.AIModelId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<AICreditGetResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(_testing.AIModelId, success.Data.AIModelId);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }

        [Fact]
        public async Task TestHandlerNotFound_ReturnsNotFound_ForUnconfiguredAIModel()
        {
            // Act
            var result = await _testing.GetAICreditByAIModelIdQueryHandler.Handle(new GetAICreditByAIModelIdQuery()
            {
                AIModelId = GetAICreditByAIModelIdQueryHandlerTesting.RandomNumber.Next()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
