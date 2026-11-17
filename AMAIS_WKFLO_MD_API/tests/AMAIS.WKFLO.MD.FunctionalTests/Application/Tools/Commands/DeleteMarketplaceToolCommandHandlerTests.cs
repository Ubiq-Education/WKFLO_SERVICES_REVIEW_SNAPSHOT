using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Commands
{
    public class DeleteMarketplaceToolCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public DeleteMarketplaceToolCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeleteMarketplaceTool_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandMarketplaceTool();
            var successArrangeResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetDeleteCommandMarketplaceTool(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceToolDeleteResult>>(result);
            var successResult = ((SuccessResult<MarketplaceToolDeleteResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task DeleteMarketplaceTool_ResultInvalid_MarketplaceToolMustExist()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetDeleteCommandMarketplaceTool(-1);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.ToolMustExist && m.Property == nameof(Tool));
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

        private static DeleteMarketplaceToolCommand GetDeleteCommandMarketplaceTool(int id)
        {
            return new DeleteMarketplaceToolCommand
            {
                Id = id
            };
        }
    }
}
