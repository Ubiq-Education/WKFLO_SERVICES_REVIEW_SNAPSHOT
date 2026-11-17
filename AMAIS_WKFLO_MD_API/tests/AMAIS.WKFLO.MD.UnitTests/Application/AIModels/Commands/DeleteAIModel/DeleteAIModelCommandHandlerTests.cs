using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.DeleteAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Commands.DeleteAIModel
{
    public class DeleteAIModelCommandHandlerTests : IClassFixture<DeleteAIModelCommandHandlerTesting>
    {
        private readonly DeleteAIModelCommandHandlerTesting _testing;

        public DeleteAIModelCommandHandlerTests(DeleteAIModelCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            // Act
            var result = await _testing.DeleteAIModelCommandHandler.Handle(new DeleteAIModelCommand()
            {
                Id = _testing.AIModelId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<AIModelDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk");
            }
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.DeleteAIModelCommandHandler.Handle(new DeleteAIModelCommand()
            {
                Id = 0
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
            if (result is SuccessResult<AIModelDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
