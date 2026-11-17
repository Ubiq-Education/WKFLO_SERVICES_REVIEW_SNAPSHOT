using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.AIModels.Queries.GetAIModelById;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.AIModels
{
    public class GetAIModelQueryEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string GetUrl = "/api/v1/ai-model";


        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task GetAIModel_ResultOk()
        {
            // Arrange
            Fixture.DeleteAIModelData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandAIModel();
            var successArrangeResult = (SuccessResult<AIModelCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<AIModelGetResult>(new AIModelGetResult()
            {
                Id = successArrangeResult.Data.Id,
                Name = "Test AI Model",
                Model = "Test Model",
                Provider = "Test Provider",
                Type = "Test Type",
                IsActive = true
            });

            // Act
            var response = await Fixture.Client.GetAsync($"{GetUrl}/{successArrangeResult.Data.Id}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<AIModelGetResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
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

        [Fact]
        public async Task GetAIModel_ResultInvalid_AIModelMustExist()
        {
            // Arrange
            Fixture.DeleteAIModelData();
            Fixture.AddTenantContext(null);
            var command = GetQueryNotExistingAIModel();

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new NotFoundResult();

            // Act
            var response = await Fixture.Client.GetAsync($"{GetUrl}/{command.Id}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var notFoundResult = JsonConvert.DeserializeObject<NotFoundResult>(responseBody);
            notFoundResult.Should().BeEquivalentTo(expected);
            // Assert
            Assert.IsType<NotFoundResult>(notFoundResult);
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

        private static GetAIModelByIdQuery GetQueryNotExistingAIModel()
        {
            return new GetAIModelByIdQuery
            {
                Id = 0
            };
        }
    }
}

