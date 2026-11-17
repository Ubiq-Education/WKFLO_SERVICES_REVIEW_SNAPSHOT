using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetMarketplaceToolById;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Queries
{
    public class GetMarketplaceToolCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task GetMarketplaceTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            //Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandMarketplaceTool();
            var successArrangeResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetGetCommandMarketplaceTool(successArrangeResult.Data.Id);

            // Act
            var result = await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceToolGetResult>>(result);
            var successResult = ((SuccessResult<MarketplaceToolGetResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task GetMarketplaceTool_ResultInvalid_MarketplaceToolMustExist()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetQueryNotExistingMarketplaceTool();

            // Act
            var result = await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        private static CreateMarketplaceToolCommand GetCreateCommandMarketplaceTool()
        {
            return new CreateMarketplaceToolCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config"
            };
        }

        private static GetMarketplaceToolByIdQuery GetGetCommandMarketplaceTool(int id)
        {
            return new GetMarketplaceToolByIdQuery
            {
                Id = id
            };
        }

        private static GetMarketplaceToolByIdQuery GetQueryNotExistingMarketplaceTool()
        {
            return new GetMarketplaceToolByIdQuery
            {
                Id = 0
            };
        }
    }
}
