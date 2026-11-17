using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Commands
{
    public class CreateTenantToolCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task CreateFirstTenantTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandFirstTenantTool();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            var successResult = ((SuccessResult<TenantToolCreateResult>)result).Data;

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
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task CreateSecondTenantTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandSecondTenantTool();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            var successResult = ((SuccessResult<TenantToolCreateResult>)result).Data;

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
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task CreateSameSecondTenantTool_ResultInvalid_ThereIsNoTenant()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command1 = GetCreateCommandSecondTenantTool();

            // Act
            var result = (Result)await mediator.Send(command1, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantMustExist && m.Property == nameof(TenantContext.TenantName));
        }

        private static CreateTenantToolCommand GetCreateCommandFirstTenantTool()
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

        private static CreateTenantToolCommand GetCreateCommandSecondTenantTool()
        {
            return new CreateTenantToolCommand
            {
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type 2",
                Config = "Test Config 2",
                IsActive = false
            };
        }
    }
}
