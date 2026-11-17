using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentById;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class GetTenantAgentQueryEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string GetUrl = "/api/v1/tenant/agent";


        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task GetTenantAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandTenantAgent();
            var successArrangeResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantAgentGetResult>(new TenantAgentGetResult()
            {
                Id = successArrangeResult.Data.Id,
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                AgentTools = [],
                IsActive = true,
                MaxToolCallsPerTurn = 3
            });

            // Act
            var response = await Fixture.Client.GetAsync($"{GetUrl}/{successArrangeResult.Data.Id}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantAgentGetResult>>(responseBody);
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
        public async Task GetTenantAgent_ResultInvalid_TenantAgentMustExist()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");
            var command = GetQueryNotExistingTenantAgent();

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

        private static CreateTenantAgentCommand GetCreateCommandTenantAgent()
        {
            return new CreateTenantAgentCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                IsActive = true
            };
        }

        private static GetTenantAgentByIdQuery GetQueryNotExistingTenantAgent()
        {
            return new GetTenantAgentByIdQuery
            {
                Id = 0
            };
        }
    }
}

