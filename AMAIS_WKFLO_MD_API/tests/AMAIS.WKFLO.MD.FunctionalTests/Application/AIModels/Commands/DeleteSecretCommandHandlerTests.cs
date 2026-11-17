using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.DeleteAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.AIModels.Commands
{
    public class DeleteAIModelCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public DeleteAIModelCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeleteAIModel_ResultOk()
        {
            // Arrange
            _fixture.DeleteAIModelData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandAIModel();
            var successArrangeResult = (SuccessResult<AIModelCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetDeleteCommandAIModel(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AIModelDeleteResult>>(result);
            var successResult = ((SuccessResult<AIModelDeleteResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task DeleteAIModel_ResultInvalid_AIModelMustExist()
        {
            // Arrange
            _fixture.DeleteAIModelData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetDeleteCommandAIModel(-1);

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

        private static DeleteAIModelCommand GetDeleteCommandAIModel(int id)
        {
            return new DeleteAIModelCommand
            {
                Id = id
            };
        }
    }
}
