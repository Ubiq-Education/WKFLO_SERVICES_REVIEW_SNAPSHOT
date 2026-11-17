using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.DeleteMarketplaceAgent
{
    public class DeleteMarketplaceAgentCommandHandlerTests : IClassFixture<DeleteMarketplaceAgentCommandHandlerTesting>
    {
        private readonly DeleteMarketplaceAgentCommandHandlerTesting _testing;

        public DeleteMarketplaceAgentCommandHandlerTests(DeleteMarketplaceAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            // Act
            var result = await _testing.DeleteMarketplaceAgentCommandHandler.Handle(new DeleteMarketplaceAgentCommand()
            {
                Id = _testing.MarketplaceAgentId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<MarketplaceAgentDeleteResult> success)
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
            var result = await _testing.DeleteMarketplaceAgentCommandHandler.Handle(new DeleteMarketplaceAgentCommand()
            {
                Id = 0
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
            if (result is SuccessResult<MarketplaceAgentDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
