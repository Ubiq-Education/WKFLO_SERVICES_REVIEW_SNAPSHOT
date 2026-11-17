using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.CreateMarketplaceTool
{
    public class CreateMarketplaceToolCommandHandlerTests : IClassFixture<CreateMarketplaceToolCommandHandlerTesting>
    {
        private readonly CreateMarketplaceToolCommandHandlerTesting _testing;

        public CreateMarketplaceToolCommandHandlerTests(CreateMarketplaceToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Name Test 1", "Description Test 1", "Type 1", "{}", ResultType.Ok)]
        [InlineData("Name Test 2", "Description Test 2", "Type 2", "{}", ResultType.Ok)]
        public async Task TestHandler(string name, string description, string type, string config, ResultType resultType)
        {
            // Act
            var result = await _testing.CreateMarketplaceToolCommandHandler.Handle(new CreateMarketplaceToolCommand()
            {
                Name = name,
                Description = description,
                Type = type,
                Config = config,
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<MarketplaceToolCreateResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
