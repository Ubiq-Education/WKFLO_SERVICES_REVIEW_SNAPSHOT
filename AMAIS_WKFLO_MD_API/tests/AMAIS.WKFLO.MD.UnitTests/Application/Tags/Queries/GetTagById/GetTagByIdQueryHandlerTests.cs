using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using AMAIS.WKFLO.MD.Application.Tags.Queries.GetTagById;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Queries.GetTagById
{
    public class GetTagByIdQueryHandlerTests(GetTagByIdQueryHandlerTesting testing) : IClassFixture<GetTagByIdQueryHandlerTesting>
    {
        private GetTagByIdQueryHandlerTesting Testing => testing;

        [Theory]
        [InlineData(1, ResultType.Ok)]
        [InlineData(99, ResultType.NotFound)]
        public async Task TestHandler(int id, ResultType resultType)
        {
            var result = await Testing.GetTagByIdQueryHandler.Handle(
                new GetTagByIdQuery { Id = id },
                TestContext.Current.CancellationToken);

            Assert.Equal(resultType, result.ResultType);
            if (result is SuccessResult<TagGetResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(id, success.Data.Id);
            }
        }
    }
}
