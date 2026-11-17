using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.StoreConversationTenantAgentById;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Agents
{
    public class StoreConversationTenantAgentCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string UrlTemplate = "/api/v1/tenant/agent/{0}/store/chat/conversation";

        [Fact]
        public async Task StoreConversation_WithValidPayload_ReturnsOk()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();

            var db = (MasterDataDbContext)Fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var tenant = db.Tenants.First(t => t.Name == "Test");

            db.PublishedTenantAgents.Add(new PublishedTenantAgent
            {
                Name = "Conv Agent 1",
                Description = "Agent for conversation tests",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            });
            db.SaveChanges();

            var agent = db.PublishedTenantAgents.First(a => a.Name == "Conv Agent 1");

            var command = new StoreConversationTenantAgentByIdCommand
            {
                SessionId = Guid.NewGuid().ToString(),
                Messages = new List<StoreConversationMessageInput>
                {
                    new StoreConversationMessageInput { Role = "user", Text = "Hello" },
                    new StoreConversationMessageInput { Role = "assistant", Text = "Hi, how can I help you?" }
                }
            };

            // Act
            var response = await SendAuthenticatedPostAsync("Test", agent.Id, command);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<bool>>(body);
            Assert.NotNull(result);
            Assert.True(result.Data);
        }

        [Fact]
        public async Task StoreConversation_CalledTwiceWithSameSessionId_IsIdempotentAndReturnsOk()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();

            var db = (MasterDataDbContext)Fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var tenant = db.Tenants.First(t => t.Name == "Test");

            db.PublishedTenantAgents.Add(new PublishedTenantAgent
            {
                Name = "Idempotent Agent",
                Description = "Agent for idempotency test",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            });
            db.SaveChanges();

            var agent = db.PublishedTenantAgents.First(a => a.Name == "Idempotent Agent");
            var sessionId = Guid.NewGuid().ToString();

            var command = new StoreConversationTenantAgentByIdCommand
            {
                SessionId = sessionId,
                Messages = new List<StoreConversationMessageInput>
                {
                    new StoreConversationMessageInput { Role = "user", Text = "First message" }
                }
            };

            // Act
            var firstResponse = await SendAuthenticatedPostAsync("Test", agent.Id, command);
            var secondResponse = await SendAuthenticatedPostAsync("Test", agent.Id, command);

            // Assert
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task StoreConversation_WithNonExistentAgent_ReturnsBadRequest()
        {
            // Arrange
            var command = new StoreConversationTenantAgentByIdCommand
            {
                SessionId = Guid.NewGuid().ToString(),
                Messages = new List<StoreConversationMessageInput>
                {
                    new StoreConversationMessageInput { Role = "user", Text = "Hello" }
                }
            };

            // Act
            var response = await SendAuthenticatedPostAsync("Test", 99999, command);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task StoreConversation_WithDeletedAgent_ReturnsBadRequest()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();

            var db = (MasterDataDbContext)Fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var tenant = db.Tenants.First(t => t.Name == "Test");

            var deletedAgent = new TenantAgent
            {
                Name = "Deleted Agent",
                Description = "Deleted agent",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = true,
                TenantId = tenant.Id
            };
            db.TenantAgents.Add(deletedAgent);
            db.SaveChanges();

            var command = new StoreConversationTenantAgentByIdCommand
            {
                SessionId = Guid.NewGuid().ToString(),
                Messages = new List<StoreConversationMessageInput>
                {
                    new StoreConversationMessageInput { Role = "user", Text = "Hello" }
                }
            };

            // Act
            var response = await SendAuthenticatedPostAsync("Test", deletedAgent.Id, command);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task StoreConversation_WithoutAuthentication_ReturnsUnauthorized()
        {
            // Arrange
            var command = new StoreConversationTenantAgentByIdCommand
            {
                SessionId = Guid.NewGuid().ToString(),
                Messages = new List<StoreConversationMessageInput>
                {
                    new StoreConversationMessageInput { Role = "user", Text = "Hello" }
                }
            };

            var url = string.Format(UrlTemplate, 1);
            var content = new StringContent(JsonConvert.SerializeObject(command), Encoding.UTF8, "application/json");

            Fixture.Client.DefaultRequestHeaders.Clear();

            // Act
            var response = await Fixture.Client.PostAsync(url, content, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task StoreConversation_WithEmptyMessages_ReturnsBadRequest()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();

            var db = (MasterDataDbContext)Fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var tenant = db.Tenants.First(t => t.Name == "Test");

            db.TenantAgents.Add(new TenantAgent
            {
                Name = "Agent Empty Msg",
                Description = "Agent for empty messages test",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            });
            db.SaveChanges();

            var agent = db.TenantAgents.First(a => a.Name == "Agent Empty Msg");

            var command = new StoreConversationTenantAgentByIdCommand
            {
                SessionId = Guid.NewGuid().ToString(),
                Messages = new List<StoreConversationMessageInput>()
            };

            // Act
            var response = await SendAuthenticatedPostAsync("Test", agent.Id, command);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task StoreConversation_WithNoTenantHeader_ReturnsBadRequest()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();

            var db = (MasterDataDbContext)Fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var tenant = db.Tenants.First(t => t.Name == "Test");

            db.TenantAgents.Add(new TenantAgent
            {
                Name = "Agent No Tenant",
                Description = "Agent for no-tenant test",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            });
            db.SaveChanges();

            var agent = db.TenantAgents.First(a => a.Name == "Agent No Tenant");

            var command = new StoreConversationTenantAgentByIdCommand
            {
                SessionId = Guid.NewGuid().ToString(),
                Messages = new List<StoreConversationMessageInput>
                {
                    new StoreConversationMessageInput { Role = "user", Text = "Hello" }
                }
            };

            var url = string.Format(UrlTemplate, agent.Id);
            var content = new StringContent(JsonConvert.SerializeObject(command), Encoding.UTF8, "application/json");

            Fixture.Client.DefaultRequestHeaders.Clear();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + TokenBuilder.BuildJWTToken());
            // No TenantName header

            // Act
            var response = await Fixture.Client.PostAsync(url, content, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(body);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid);
        }

        private async Task<HttpResponseMessage> SendAuthenticatedPostAsync(string tenantName, int agentId, StoreConversationTenantAgentByIdCommand command)
        {
            var url = string.Format(UrlTemplate, agentId);
            var content = new StringContent(JsonConvert.SerializeObject(command), Encoding.UTF8, "application/json");

            Fixture.Client.DefaultRequestHeaders.Clear();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + TokenBuilder.BuildJWTToken());
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, tenantName);

            return await Fixture.Client.PostAsync(url, content, TestContext.Current.CancellationToken);
        }
    }
}

