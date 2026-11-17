using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.AIModels.Queries.ListAIModels;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.AIModels.Queries
{
    public class ListAIModelCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task ListAIModel_ResultOk()
        {
            // Arrange
            Fixture.DeleteAIModelData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = ListCreateCommandAIModel();
            var successArrangeResult = (SuccessResult<AIModelCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetListCommandAIModel();

            // Act
            var result = await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<List<AIModelGetResult>>>(result);
            var successResult = ((SuccessResult<List<AIModelGetResult>>)result).Data;

            Assert.IsType<int>(successResult[0].Id);
            Assert.NotEqual(default, successResult[0].Id);
        }

        private static CreateAIModelCommand ListCreateCommandAIModel()
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

        private static ListAIModelsQuery GetListCommandAIModel()
        {
            return new ListAIModelsQuery
            {
            };
        }
    }
}
