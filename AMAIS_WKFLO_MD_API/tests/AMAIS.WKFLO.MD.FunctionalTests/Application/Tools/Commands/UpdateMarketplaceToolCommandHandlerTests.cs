using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Commands
{
    public class UpdateMarketplaceToolCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public UpdateMarketplaceToolCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task UpdateMarketplaceTool_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandMarketplaceTool();
            var successArrangeResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandMarketplaceTool(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceToolUpdateResult>>(result);
            var successResult = ((SuccessResult<MarketplaceToolUpdateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command2.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command2.Description, successResult.Description);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command2.Type, successResult.Type);
            Assert.NotNull(successResult.Config);
            Assert.Equal(command2.Config, successResult.Config);
        }

        [Fact]
        public async Task UpdateMarketplaceTool_ResultInvalid_MarketplaceToolMustExist()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetUpdateCommandMarketplaceTool(-1);

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

        private static UpdateMarketplaceToolCommand GetUpdateCommandMarketplaceTool(int id)
        {
            return new UpdateMarketplaceToolCommand
            {
                Id = id,
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type 2",
                Config = "Test Config 2",
            };
        }
    }
}
