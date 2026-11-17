using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Commands
{
    public class CreateMarketplaceToolCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task CreateFirstMarketplaceTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            //Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandFirstMarketplaceTool();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceToolCreateResult>>(result);
            var successResult = ((SuccessResult<MarketplaceToolCreateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command.Description, successResult.Description);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command.Type, successResult.Type);
            Assert.NotNull(successResult.Config);
            Assert.Equal(command.Config, successResult.Config);
        }

        [Fact]
        public async Task CreateSecondMarketplaceTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            //Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandSecondMarketplaceTool();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceToolCreateResult>>(result);
            var successResult = ((SuccessResult<MarketplaceToolCreateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command.Description, successResult.Description);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command.Type, successResult.Type);
            Assert.NotNull(successResult.Config);
            Assert.Equal(command.Config, successResult.Config);
        }

        private static CreateMarketplaceToolCommand GetCreateCommandFirstMarketplaceTool()
        {
            return new CreateMarketplaceToolCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config"
            };
        }

        private static CreateMarketplaceToolCommand GetCreateCommandSecondMarketplaceTool()
        {
            return new CreateMarketplaceToolCommand
            {
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type 2",
                Config = "Test Config 2"
            };
        }
    }
}
