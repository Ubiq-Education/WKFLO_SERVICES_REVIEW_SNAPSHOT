using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Commands
{
    public class DeleteTenantToolCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public DeleteTenantToolCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeleteTenantTool_ResultOk()
        {
            // Arrange
            _fixture.DeleteTenantToolData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandTenantTool();
            var successArrangeResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetDeleteCommandTenantTool(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolDeleteResult>>(result);
            var successResult = ((SuccessResult<TenantToolDeleteResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task DeleteTenantTool_ResultInvalid_TenantToolMustExist()
        {
            // Arrange
            _fixture.DeleteTenantToolData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetDeleteCommandTenantTool(-1);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.ToolMustExist && m.Property == nameof(Tool));
        }

        [Fact]
        public async Task DeleteTenantTool_ResultInvalid_ThereIsNoTenant()
        {
            // Arrange
            _fixture.DeleteTenantToolData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetDeleteCommandNotExistingTenantTool();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.ToolMustExist && m.Property == nameof(Tool));
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
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

        private static DeleteTenantToolCommand GetDeleteCommandTenantTool(int id)
        {
            return new DeleteTenantToolCommand
            {
                Id = id
            };
        }

        private static DeleteTenantToolCommand GetDeleteCommandNotExistingTenantTool()
        {
            return new DeleteTenantToolCommand
            {
                Id = 0
            };
        }
    }
}
