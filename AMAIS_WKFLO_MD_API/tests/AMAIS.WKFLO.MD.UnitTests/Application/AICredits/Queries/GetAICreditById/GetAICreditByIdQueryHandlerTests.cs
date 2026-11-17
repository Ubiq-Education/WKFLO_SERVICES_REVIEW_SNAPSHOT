using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditById;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Queries.GetAICreditById
{
    public class GetAICreditByIdQueryHandlerTests : IClassFixture<GetAICreditByIdQueryHandlerTesting>
    {
        private readonly GetAICreditByIdQueryHandlerTesting _testing;

        public GetAICreditByIdQueryHandlerTests(GetAICreditByIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.GetAICreditByIdQueryHandler.Handle(new GetAICreditByIdQuery()
            {
                Id = _testing.AICreditId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<AICreditGetResult> success)
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
            var result = await _testing.GetAICreditByIdQueryHandler.Handle(new GetAICreditByIdQuery()
            {
                Id = GetAICreditByIdQueryHandlerTesting.RandomNumber.Next()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
