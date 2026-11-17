using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.AIModels.Queries.GetAIModelById;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Queries.GetAIModelById
{
    public class GetAIModelByIdQueryHandlerTests : IClassFixture<GetAIModelByIdQueryHandlerTesting>
    {
        private readonly GetAIModelByIdQueryHandlerTesting _testing;

        public GetAIModelByIdQueryHandlerTests(GetAIModelByIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.GetAIModelByIdQueryHandler.Handle(new GetAIModelByIdQuery()
            {
                Id = _testing.AIModelId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<AIModelGetResult> success)
            {
                Assert.NotNull(success.Data);
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
            var result = await _testing.GetAIModelByIdQueryHandler.Handle(new GetAIModelByIdQuery()
            {
                Id = GetAIModelByIdQueryHandlerTesting.RandomNumber.Next()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
