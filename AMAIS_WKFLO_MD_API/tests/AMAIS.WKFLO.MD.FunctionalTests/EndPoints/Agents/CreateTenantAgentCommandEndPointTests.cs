using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class CreateTenantAgentCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string CreateUrl = "/api/v1/tenant/agent";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task CreateFirstTenantAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            var command = GetCreateCommandFirstTenantAgent();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantAgentCreateResult>(new TenantAgentCreateResult()
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                IsActive = true,
                EnableSpinUpAgents = true,
                MaxToolCallsPerTurn = 3
            });

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(responseBody);
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
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task CreateSecondTenantAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            var command = GetCreateCommandSecondTenantAgent();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantAgentCreateResult>(new TenantAgentCreateResult()
            {
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2",
                ResponseTone = "Friendly 2",
                IsActive = true,
                EnableSpinUpAgents = true,
                MaxToolCallsPerTurn = 3
            });

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantAgentCreateResult>>(responseBody);
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
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task CreateSameSecondTenantAgent_ResultInvalid_ThereIsNoTenant()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            var command = GetCreateCommandFirstTenantAgent();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            // No headers for Tenant Context
            //_fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult();
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid);
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantMustExist);

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
        }

        private static CreateTenantAgentCommand GetCreateCommandFirstTenantAgent()
        {
            return new CreateTenantAgentCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                IsActive = true,
                EnableSpinUpAgents = true
            };
        }

        private static CreateTenantAgentCommand GetCreateCommandSecondTenantAgent()
        {
            return new CreateTenantAgentCommand
            {
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2",
                ResponseTone = "Friendly 2",
                IsActive = true,
                EnableSpinUpAgents = true
            };
        }
    }
}

