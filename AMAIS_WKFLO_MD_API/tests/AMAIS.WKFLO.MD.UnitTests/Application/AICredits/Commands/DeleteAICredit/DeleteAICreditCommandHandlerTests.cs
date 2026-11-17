using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.DeleteAICredit;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.DeleteAICredit
{
    public class DeleteAICreditCommandHandlerTests : IClassFixture<DeleteAICreditCommandHandlerTesting>
    {
        private readonly DeleteAICreditCommandHandlerTesting _testing;

        public DeleteAICreditCommandHandlerTests(DeleteAICreditCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            // Act
            var result = await _testing.DeleteAICreditCommandHandler.Handle(new DeleteAICreditCommand()
            {
                Id = _testing.AICreditId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<AICreditDeleteResult> success)
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
            var result = await _testing.DeleteAICreditCommandHandler.Handle(new DeleteAICreditCommand()
            {
                Id = 0
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
