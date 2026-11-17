using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.UpdateTag;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Commands.UpdateTag
{
    public class UpdateTagCommandHandlerTests(UpdateTagCommandHandlerTesting testing) : IClassFixture<UpdateTagCommandHandlerTesting>
    {
        private UpdateTagCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData(1, "updated-name", "#AABBCC", ResultType.Ok)]
        [InlineData(1, "another-name", null, ResultType.Ok)]
        [InlineData(99, "ghost-tag", "#000000", ResultType.NotFound)]
        public async Task TestHandler(int id, string name, string color, ResultType resultType)
        {
            var result = await Testing.UpdateTagCommandHandler.Handle(new UpdateTagCommand
            {
                Id = id,
                Name = name,
                Color = color
            }, TestContext.Current.CancellationToken);

            Assert.Equal(resultType, result.ResultType);
            if (result is SuccessResult<TagUpdateResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(name, success.Data.Name);
            }
        }
    }
}
