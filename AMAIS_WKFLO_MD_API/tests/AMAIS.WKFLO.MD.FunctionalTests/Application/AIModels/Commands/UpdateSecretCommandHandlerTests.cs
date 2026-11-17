using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.UpdateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.AIModels.Commands
{
    public class UpdateAIModelCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public UpdateAIModelCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task UpdateAIModel_ResultOk()
        {
            // Arrange
            _fixture.DeleteAIModelData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandAIModel();
            var successArrangeResult = (SuccessResult<AIModelCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandAIModel(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AIModelUpdateResult>>(result);
            var successResult = ((SuccessResult<AIModelUpdateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command2.Name, successResult.Name);
            Assert.NotNull(successResult.Model);
            Assert.Equal(command2.Model, successResult.Model);
            Assert.NotNull(successResult.Provider);
            Assert.Equal(command2.Provider, successResult.Provider);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command2.Type, successResult.Type);
            Assert.Equal(command2.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task UpdateAIModel_ResultInvalid_AIModelMustExist()
        {
            // Arrange
            _fixture.DeleteAIModelData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetUpdateCommandAIModel(-1);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AIModelMustExist && m.Property == nameof(AIModel));
        }

        private static CreateAIModelCommand GetCreateCommandAIModel()
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

        private static UpdateAIModelCommand GetUpdateCommandAIModel(int id)
        {
            return new UpdateAIModelCommand
            {
                Id = id,
                Name = "Test AI Model 2",
                Model = "Test Model 2",
                Provider = "Test Provider 2",
                Type = "Test Type 2",
                IsActive = false
            };
        }
    }
}
