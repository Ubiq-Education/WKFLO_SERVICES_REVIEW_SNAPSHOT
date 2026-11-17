using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.AIModels
{
    public class ListAIModelQueryEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string ListUrl = "/api/v1/ai-models";


        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task ListAIModel_ResultOk()
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

            var expected = new SuccessResult<List<AIModelGetResult>>(
            [
                new AIModelGetResult()
                {
                    Id = successArrangeResult.Data.Id,
                    Name = "Test AI Model",
                    Model = "Test Model",
                    Provider = "Test Provider",
                    Type = "Test Type",
                    IsActive = true
                }
            ]);

            // Act
            var response = await Fixture.Client.GetAsync($"{ListUrl}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObjects = JsonConvert.DeserializeObject<SuccessResult<List<AIModelGetResult>>>(responseBody);
            expected.Data[0].Id = responseObjects.Data[0].Id;
            expected.Data[0].CreatedOn = responseObjects.Data[0].CreatedOn;
            responseObjects.Should().BeEquivalentTo(expected);
            var successResult = responseObjects.Data[0];
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
    }
}

