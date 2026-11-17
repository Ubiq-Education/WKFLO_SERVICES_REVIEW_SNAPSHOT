using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.UpdateMarketplaceTool
{
    public class UpdateMarketplaceToolCommandHandlerTests : IClassFixture<UpdateMarketplaceToolCommandHandlerTesting>
    {
        private readonly UpdateMarketplaceToolCommandHandlerTesting _testing;

        public UpdateMarketplaceToolCommandHandlerTests(UpdateMarketplaceToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Name Test 1", "Description Test 1", "Type 1", "{}", ResultType.Ok)]
        [InlineData("Name Test 2", "Description Test 2", "Type 2", "{}", ResultType.Ok)]
        public async Task TestHandler(string name, string description, string type, string config, ResultType resultType)
        {
            // Act
            var result = await _testing.UpdateMarketplaceToolCommandHandler.Handle(new UpdateMarketplaceToolCommand()
            {
                Id = _testing.MarketplaceToolId,
                Name = name,
                Description = description,
                Type = type,
                Config = config,
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<MarketplaceToolUpdateResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
