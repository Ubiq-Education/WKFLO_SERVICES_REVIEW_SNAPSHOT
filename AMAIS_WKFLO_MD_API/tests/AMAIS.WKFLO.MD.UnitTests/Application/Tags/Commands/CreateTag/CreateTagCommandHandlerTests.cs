using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Commands.CreateTag;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Commands.CreateTag
{
    public class CreateTagCommandHandlerTests(CreateTagCommandHandlerTesting testing) : IClassFixture<CreateTagCommandHandlerTesting>
    {
        private CreateTagCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData("frontend", "#FF5733", ResultType.Ok)]
        [InlineData("backend", null, ResultType.Ok)]
        [InlineData("important", "#0000FF", ResultType.Ok)]
        public async Task TestHandler(string name, string color, ResultType resultType)
        {
            var result = await Testing.CreateTagCommandHandler.Handle(new CreateTagCommand
            {
                Name = name,
                Color = color
            }, TestContext.Current.CancellationToken);

            Assert.Equal(resultType, result.ResultType);
            if (result is SuccessResult<TagCreateResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(name, success.Data.Name);
                Assert.Equal(color, success.Data.Color);
            }
        }
    }
}
