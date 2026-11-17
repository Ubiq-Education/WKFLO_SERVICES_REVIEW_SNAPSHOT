using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Commands
{
    public class UpdateTenantToolCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public UpdateTenantToolCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task UpdateTenantTool_ResultOk()
        {
            // Arrange
            _fixture.DeleteTenantToolData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandTenantTool();
            var successArrangeResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandTenantTool(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            var successResult = ((SuccessResult<TenantToolUpdateResult>)result).Data;

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
            Assert.Equal(command2.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task UpdateTenantTool_ResultInvalid_TenantToolMustExist()
        {
            // Arrange
            _fixture.DeleteTenantToolData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetUpdateCommandTenantTool(-1);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.ToolMustExist && m.Property == nameof(Tool));
        }

        [Fact]
        public async Task UpdateTenantTool_ResultInvalid_ThereIsNoTenant()
        {
            // Arrange
            _fixture.DeleteTenantToolData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command1 = GetUpdateCommandNotExistingTenantTool();

            // Act
            var result = (Result)await mediator.Send(command1, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.ToolMustExist && m.Property == nameof(Tool));
        }


        private static CreateTenantToolCommand GetCreateCommandTenantTool()
        {
            return new CreateTenantToolCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config",
                IsActive = true
            };
        }

        private static UpdateTenantToolCommand GetUpdateCommandTenantTool(int id)
        {
            return new UpdateTenantToolCommand
            {
                Id = id,
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type 2",
                Config = "Test Config 2",
                IsActive = false
            };
        }

        private static UpdateTenantToolCommand GetUpdateCommandNotExistingTenantTool()
        {
            return new UpdateTenantToolCommand
            {
                Id = 0,
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config",
                IsActive = true
            };
        }
    }
}
