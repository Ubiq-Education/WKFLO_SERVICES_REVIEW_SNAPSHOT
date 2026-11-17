using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class DeleteTenantAgentCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string DeleteUrl = "/api/v1/tenant/agent";


        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task DeleteTenantAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = DeleteCreateCommandTenantAgent();
            var successArrangeResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantAgentDeleteResult>(new TenantAgentDeleteResult()
            {
                Id = successArrangeResult.Data.Id,
            });

            // Act
            var response = await Fixture.Client.DeleteAsync($"{DeleteUrl}/{successArrangeResult.Data.Id}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantAgentDeleteResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
        }

        [Fact]
        public async Task DeleteTenantAgent_ResultInvalid_TenantAgentMustExist()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");
            var command = DeleteTenantAgentCommandNotExisting();

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult();
            expected.AddError(nameof(Agent), ErrorConst.AgentMustExist);

            // Act
            var response = await Fixture.Client.DeleteAsync($"{DeleteUrl}/{command.Id}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            // Assert
            Assert.IsType<InvalidResult>(invalidResult);
        }

        private static CreateTenantAgentCommand DeleteCreateCommandTenantAgent()
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

        private static DeleteTenantAgentCommand DeleteTenantAgentCommandNotExisting()
        {
            return new DeleteTenantAgentCommand
            {
                Id = 2
            };
        }
    }
}

