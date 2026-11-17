using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.DeleteTag;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Commands.DeleteTag
{
    public class DeleteTagCommandHandlerTests(DeleteTagCommandHandlerTesting testing) : IClassFixture<DeleteTagCommandHandlerTesting>
    {
        private DeleteTagCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData(1, ResultType.Ok)]
        [InlineData(99, ResultType.NotFound)]
        public async Task TestHandler(int id, ResultType resultType)
        {
            var result = await Testing.DeleteTagCommandHandler.Handle(new DeleteTagCommand
            {
                Id = id
            }, TestContext.Current.CancellationToken);

            Assert.Equal(resultType, result.ResultType);
            if (result is SuccessResult<TagDeleteResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(id, success.Data.Id);
            }
        }
    }
}
