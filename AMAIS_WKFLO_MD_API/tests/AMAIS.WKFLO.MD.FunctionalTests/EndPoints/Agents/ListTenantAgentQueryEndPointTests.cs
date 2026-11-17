using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class ListTenantAgentQueryEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string ListUrl = "/api/v1/tenant/agents";


        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task ListTenantAgent_ResultOk()
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

            var expected = new SuccessResult<List<TenantAgentGetResult>>(
            [
                new TenantAgentGetResult()
                {
                    Id = successArrangeResult.Data.Id,
                    Name = "Test Name",
                    Description = "Test Description",
                    SystemPrompt = "Test System Prompt",
                    ResponseTone = "Friendly",
                    AgentTools = [],
                    IsActive = true,
                    MaxToolCallsPerTurn = 3
                }
            ]);

            // Act
            var response = await Fixture.Client.GetAsync($"{ListUrl}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObjects = JsonConvert.DeserializeObject<SuccessResult<List<TenantAgentGetResult>>>(responseBody);
            expected.Data[0].Id = responseObjects.Data[0].Id;
            expected.Data[0].CreatedOn = responseObjects.Data[0].CreatedOn;
            responseObjects.Should().BeEquivalentTo(expected);
            var successResult = responseObjects.Data[0];
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
        public async Task ListTenantAgent_ResultInvalid_TenantAgentMustExist()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            //Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult();
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid);
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantMustExist);

            // Act
            var response = await Fixture.Client.GetAsync($"{ListUrl}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            // Assert
            Assert.IsType<InvalidResult>(invalidResult);
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
    }
}

