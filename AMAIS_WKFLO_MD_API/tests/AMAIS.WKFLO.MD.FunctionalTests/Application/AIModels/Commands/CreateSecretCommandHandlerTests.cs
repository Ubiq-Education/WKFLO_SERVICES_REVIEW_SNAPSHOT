using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.AIModels.Commands
{
    public class CreateAIModelCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task CreateFirstAIModel_ResultOk()
        {
            // Arrange
            Fixture.DeleteAIModelData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandFirstAIModel();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AIModelCreateResult>>(result);
            var successResult = ((SuccessResult<AIModelCreateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Model);
            Assert.Equal(command.Model, successResult.Model);
            Assert.NotNull(successResult.Provider);
            Assert.Equal(command.Provider, successResult.Provider);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command.Type, successResult.Type);
        }

        [Fact]
        public async Task CreateSecondAIModel_ResultOk()
        {
            // Arrange
            Fixture.DeleteAIModelData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandSecondAIModel();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AIModelCreateResult>>(result);
            var successResult = ((SuccessResult<AIModelCreateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Model);
            Assert.Equal(command.Model, successResult.Model);
            Assert.NotNull(successResult.Provider);
            Assert.Equal(command.Provider, successResult.Provider);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command.Type, successResult.Type);
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        private static CreateAIModelCommand GetCreateCommandFirstAIModel()
        {
            return new CreateAIModelCommand
            {
                Name = "Test AI Model",
                Model = "Test Model",
                Provider = "Test Provider",
                Type = "Test Type",
                IsActive = true
            };
        }

        private static CreateAIModelCommand GetCreateCommandSecondAIModel()
        {
            return new CreateAIModelCommand
            {
                Name = "Test AI Model 2",
                Model = "Test Model 2",
                Provider = "Test Provider 2",
                Type = "Test Type 2",
                IsActive = false
            };
        }
    }
}
