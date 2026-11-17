using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class CreateMarketplaceAgentCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string CreateUrl = "/api/v1/marketplace/agent";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task CreateFirstMarketplaceAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            var command = GetCreateCommandFirstMarketplaceAgent();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<MarketplaceAgentCreateResult>(new MarketplaceAgentCreateResult()
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt"
            });

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<MarketplaceAgentCreateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command.Description, successResult.Description);
            Assert.NotNull(successResult.SystemPrompt);
            Assert.Equal(command.SystemPrompt, successResult.SystemPrompt);
        }

        [Fact]
        public async Task CreateSecondMarketplaceAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            var command = GetCreateCommandSecondMarketplaceAgent();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<MarketplaceAgentCreateResult>(new MarketplaceAgentCreateResult()
            {
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2"
            });

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<MarketplaceAgentCreateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command.Description, successResult.Description);
            Assert.NotNull(successResult.SystemPrompt);
            Assert.Equal(command.SystemPrompt, successResult.SystemPrompt);
        }

        private static CreateMarketplaceAgentCommand GetCreateCommandFirstMarketplaceAgent()
        {
            return new CreateMarketplaceAgentCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt"
            };
        }

        private static CreateMarketplaceAgentCommand GetCreateCommandSecondMarketplaceAgent()
        {
            return new CreateMarketplaceAgentCommand
            {
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2"
            };
        }
    }
}

