using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.DeleteMarketplaceTool
{
    public class DeleteMarketplaceToolCommandHandlerTests : IClassFixture<DeleteMarketplaceToolCommandHandlerTesting>
    {
        private readonly DeleteMarketplaceToolCommandHandlerTesting _testing;

        public DeleteMarketplaceToolCommandHandlerTests(DeleteMarketplaceToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            // Act
            var result = await _testing.DeleteMarketplaceToolCommandHandler.Handle(new DeleteMarketplaceToolCommand()
            {
                Id = _testing.MarketplaceToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<MarketplaceToolDeleteResult> success)
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
            var result = await _testing.DeleteMarketplaceToolCommandHandler.Handle(new DeleteMarketplaceToolCommand()
            {
                Id = 0
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
            if (result is SuccessResult<MarketplaceToolDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
