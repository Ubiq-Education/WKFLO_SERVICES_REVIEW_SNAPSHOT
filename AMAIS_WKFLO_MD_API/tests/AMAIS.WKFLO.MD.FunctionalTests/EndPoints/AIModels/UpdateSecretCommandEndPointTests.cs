using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.UpdateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.AIModels
{
    public class UpdateAIModelCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string UpdateUrl = "/api/v1/ai-model";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task UpdateAIModel_ResultOk()
        {
            // Arrange
            Fixture.DeleteAIModelData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandAIModel();
            var successArrangeResult = (SuccessResult<AIModelCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandAIModel(successArrangeResult.Data.Id);

            var json = JsonConvert.SerializeObject(command2);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<AIModelUpdateResult>(new AIModelUpdateResult()
            {
                Id = successArrangeResult.Data.Id,
                Name = "Test AI Model 2",
                Model = "Test Model 2",
                Provider = "Test Provider 2",
                Type = "Test Type 2",
                IsActive = false
            });

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/{successArrangeResult.Data.Id}", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<AIModelUpdateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            expected.Data.LastUpdatedOn = responseObject.Data.LastUpdatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
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
            Fixture.DeleteAIModelData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command1 = GetUpdateCommandAIModel(1);

            var json = JsonConvert.SerializeObject(command1);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult(nameof(AIModel), ErrorConst.AIModelMustExist);

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/1", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
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

        private static UpdateAIModelCommand GetUpdateCommandNotExistingAIModel()
        {
            return new UpdateAIModelCommand
            {
                Id = 1,
                Name = "Test AI Model 2",
                Model = "Test Model 2",
                Provider = "Test Provider 2",
                Type = "Test Type 2",
                IsActive = true
            };
        }
    }
}

