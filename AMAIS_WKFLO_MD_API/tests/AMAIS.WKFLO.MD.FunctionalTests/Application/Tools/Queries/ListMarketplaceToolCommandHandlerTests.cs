using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.ListMarketplaceTools;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Queries
{
    public class ListMarketplaceToolCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task ListMarketplaceTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            //Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = ListCreateCommandMarketplaceTool();
            var successArrangeResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetListCommandMarketplaceTool();

            // Act
            var result = await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<List<MarketplaceToolGetResult>>>(result);
            var successResult = ((SuccessResult<List<MarketplaceToolGetResult>>)result).Data;

            Assert.IsType<int>(successResult[0].Id);
            Assert.NotEqual(default, successResult[0].Id);
        }

        private static CreateMarketplaceToolCommand ListCreateCommandMarketplaceTool()
        {
            return new CreateMarketplaceToolCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config"
            };
        }

        private static ListMarketplaceToolsQuery GetListCommandMarketplaceTool()
        {
            return new ListMarketplaceToolsQuery
            {
            };
        }
    }
}
