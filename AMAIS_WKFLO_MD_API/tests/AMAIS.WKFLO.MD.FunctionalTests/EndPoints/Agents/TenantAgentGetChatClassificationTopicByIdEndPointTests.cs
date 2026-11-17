using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgentToolById;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class TenantAgentGetChatClassificationTopicByIdEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private void AuthenticateAsTenant(string tenantName)
        {
            var token = TokenBuilder.BuildJWTToken();
            Fixture.Client.DefaultRequestHeaders.Clear();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenantName);
        }

        private async Task<(int AgentId, int ToolId)> CreateAgentWithChatClassificationToolAsync(string agentName, string configJson)
        {
            var mediator = Fixture.GetMediator();

            var agentResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(new CreateTenantAgentCommand
            {
                Name = agentName,
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                IsActive = true
            }, TestContext.Current.CancellationToken);

            var toolResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(new CreateTenantToolCommand
            {
                Name = "Chat Classification",
                Description = "Test Description",
                Type = "Chat_Classification",
                Config = configJson,
                IsActive = true
            }, TestContext.Current.CancellationToken);

            await mediator.Send(new CreateTenantAgentToolByIdCommand
            {
                Id = agentResult.Data.Id,
                ToolId = toolResult.Data.Id
            }, TestContext.Current.CancellationToken);

            return (agentResult.Data.Id, toolResult.Data.Id);
        }

        [Fact]
        public async Task GetChatClassificationTopicById_ResultOk()
        {
            // Arrange
            Fixture.DeleteAllAgentData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var topicId = Guid.NewGuid();
            var configJson = JsonConvert.SerializeObject(new
            {
                topics = new[]
                {
                    new { id = topicId, name = "Billing", description = "Billing questions", isActive = true, handoffMessage = "Transferring you to billing." }
                },
                version = 1
            });

            var (agentId, toolId) = await CreateAgentWithChatClassificationToolAsync("Test Agent", configJson);
            AuthenticateAsTenant("Test");

            // Act
            var response = await Fixture.Client.GetAsync(
                $"/api/v1/tenant/agent/{agentId}/tools/chat-classification/topics/{topicId}",
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<ChatClassificationTopicGetResult>>(responseBody);
            Assert.NotNull(responseObject?.Data);
            Assert.Equal(topicId, responseObject.Data.Id);
            Assert.Equal("Billing", responseObject.Data.Name);
            Assert.True(responseObject.Data.IsActive);
            Assert.Equal(agentId, responseObject.Data.AgentId);
            Assert.Equal(toolId, responseObject.Data.ToolId);
        }

        [Fact]
        public async Task GetChatClassificationTopicById_ResultNotFound_WhenTopicIdIsUnknown()
        {
            // Arrange
            Fixture.DeleteAllAgentData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var configJson = JsonConvert.SerializeObject(new
            {
                topics = new[]
                {
                    new { id = Guid.NewGuid(), name = "Billing", description = "Billing questions", isActive = true, handoffMessage = (string)null }
                },
                version = 1
            });

            var (agentId, _) = await CreateAgentWithChatClassificationToolAsync("Test Agent 2", configJson);
            AuthenticateAsTenant("Test");

            // Act
            var response = await Fixture.Client.GetAsync(
                $"/api/v1/tenant/agent/{agentId}/tools/chat-classification/topics/{Guid.NewGuid()}",
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetChatClassificationTopicById_ResultNotFound_WhenAgentHasNoChatClassificationTool()
        {
            // Arrange
            Fixture.DeleteAllAgentData();
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var agentResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(new CreateTenantAgentCommand
            {
                Name = "Test Agent 3",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                IsActive = true
            }, TestContext.Current.CancellationToken);

            AuthenticateAsTenant("Test");

            // Act
            var response = await Fixture.Client.GetAsync(
                $"/api/v1/tenant/agent/{agentResult.Data.Id}/tools/chat-classification/topics/{Guid.NewGuid()}",
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
