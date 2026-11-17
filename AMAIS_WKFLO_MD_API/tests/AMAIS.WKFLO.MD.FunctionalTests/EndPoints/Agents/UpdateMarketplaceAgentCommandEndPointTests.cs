using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateMarketplaceAgent;
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
    public class UpdateMarketplaceAgentCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string UpdateUrl = "/api/v1/marketplace/agent";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task UpdateMarketplaceAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandMarketplaceAgent();
            var successArrangeResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandMarketplaceAgent(successArrangeResult.Data.Id);

            var json = JsonConvert.SerializeObject(command2);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<MarketplaceAgentUpdateResult>(new MarketplaceAgentUpdateResult()
            {
                Id = successArrangeResult.Data.Id,
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2",
                AgentTools = []
            });

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/{successArrangeResult.Data.Id}", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<MarketplaceAgentUpdateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            expected.Data.LastUpdatedOn = responseObject.Data.LastUpdatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command2.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command2.Description, successResult.Description);
            Assert.NotNull(successResult.SystemPrompt);
            Assert.Equal(command2.SystemPrompt, successResult.SystemPrompt);
        }

        [Fact]
        public async Task UpdateMarketplaceAgent_ResultInvalid_MarketplaceAgentMustExist()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            var command1 = GetUpdateCommandMarketplaceAgent(1);

            var json = JsonConvert.SerializeObject(command1);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            //Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid)
                .AddError(nameof(TenantContext.TenantName), ErrorConst.TenantMustExist)
                .AddError(nameof(Agent), ErrorConst.AgentMustExist);

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/1", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AgentMustExist && m.Property == nameof(Agent));
        }

        private static CreateMarketplaceAgentCommand GetCreateCommandMarketplaceAgent()
        {
            return new CreateMarketplaceAgentCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt"
            };
        }

        private static UpdateMarketplaceAgentCommand GetUpdateCommandMarketplaceAgent(int id)
        {
            return new UpdateMarketplaceAgentCommand
            {
                Id = id,
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2"
            };
        }
    }
}

