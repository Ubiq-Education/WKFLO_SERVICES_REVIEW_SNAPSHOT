using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.WipeLongTermMemoryTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class WipeLongTermMemoryTenantAgentCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string UrlTemplate = "/api/v1/tenant/agent/{0}/memory/long-term";

        [Fact]
        public async Task WipeLongTermMemory_WithConversations_ReturnsOk()
        {
            // Arrange
            Fixture.DeleteConversationData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var db = (MasterDataDbContext)Fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var tenant = db.Tenants.First(t => t.Name == "Test");

            var agent = new TenantAgent
            {
                Name = "WipeLongTerm Agent",
                Description = "Agent for wipe long term memory test",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            };
            db.TenantAgents.Add(agent);
            db.SaveChanges();

            // Two distinct users (third row duplicates user-1 to verify Distinct)
            db.Conversations.Add(new Conversation { AgentId = agent.Id, TenantId = tenant.Id, SessionId = Guid.NewGuid().ToString(), UserId = "user-1" });
            db.Conversations.Add(new Conversation { AgentId = agent.Id, TenantId = tenant.Id, SessionId = Guid.NewGuid().ToString(), UserId = "user-2" });
            db.Conversations.Add(new Conversation { AgentId = agent.Id, TenantId = tenant.Id, SessionId = Guid.NewGuid().ToString(), UserId = "user-1" });
            db.SaveChanges();

            // Act
            var response = await SendAuthenticatedDeleteAsync("Test", agent.Id);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<TenantAgentWipeLongTermMemoryResult>>(body);
            Assert.NotNull(result);
            Assert.Equal(agent.Id, result.Data.AgentId);
            Assert.Equal(2, result.Data.AffectedUsers);
        }

        [Fact]
        public async Task WipeLongTermMemory_WithNoConversations_ReturnsOkWithZero()
        {
            // Arrange
            Fixture.DeleteConversationData();
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var db = (MasterDataDbContext)Fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var tenant = db.Tenants.First(t => t.Name == "Test");

            var agent = new TenantAgent
            {
                Name = "WipeLongTerm NoConv Agent",
                Description = "Agent with no conversations",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            };
            db.TenantAgents.Add(agent);
            db.SaveChanges();

            // Act
            var response = await SendAuthenticatedDeleteAsync("Test", agent.Id);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<TenantAgentWipeLongTermMemoryResult>>(body);
            Assert.NotNull(result);
            Assert.Equal(0, result.Data.AffectedUsers);
        }

        [Fact]
        public async Task WipeLongTermMemory_AgentNotExist_ReturnsBadRequest()
        {
            // Arrange
            Fixture.DeleteConversationData();
            Fixture.DeleteTenantAgentData();

            var token = TokenBuilder.BuildJWTToken();
            Fixture.Client.DefaultRequestHeaders.Clear();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult();
            expected.AddError(nameof(WipeLongTermMemoryTenantAgentCommand.Id), ErrorConst.AgentMustExist);

            // Act
            var response = await Fixture.Client.DeleteAsync(string.Format(UrlTemplate, 99999), TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(body);
            invalidResult.Should().BeEquivalentTo(expected);
        }

        [Fact]
        public async Task WipeLongTermMemory_Unauthorized_ReturnsUnauthorized()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();

            // Act
            var response = await Fixture.Client.DeleteAsync(string.Format(UrlTemplate, 1), TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task WipeLongTermMemory_NoTenantHeader_ReturnsBadRequest()
        {
            // Arrange
            Fixture.Client.DefaultRequestHeaders.Clear();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + TokenBuilder.BuildJWTToken());
            // No TenantName header

            // Act
            var response = await Fixture.Client.DeleteAsync(string.Format(UrlTemplate, 1), TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(body);
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.TenantInvalid);
        }

        private async Task<HttpResponseMessage> SendAuthenticatedDeleteAsync(string tenantName, int agentId)
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + TokenBuilder.BuildJWTToken());
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenantName);

            return await Fixture.Client.DeleteAsync(string.Format(UrlTemplate, agentId), TestContext.Current.CancellationToken);
        }
    }
}

