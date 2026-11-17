using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.StoreConversationTenantAgentById;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Commands
{
    public class StoreConversationTenantAgentByIdCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task StoreConversation_WithValidPayload_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");

            var db = (MasterDataDbContext)Fixture.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var tenant = db.Tenants.First(t => t.Name == "Test");

            db.PublishedTenantAgents.Add(new PublishedTenantAgent
            {
                Name = "Store Conversation Agent",
                Description = "Test Description",
                SystemPrompt = "Test Prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            });
            db.SaveChanges();

            var agent = db.PublishedTenantAgents.First(a => a.Name == "Store Conversation Agent");
            var mediator = Fixture.GetMediator();
            var command = BuildCommand(agent.Id, Guid.NewGuid().ToString(), "user", "Hello");

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            Assert.True(((SuccessResult<bool>)result).Data);
        }

        [Fact]
        public async Task StoreConversation_WithNonExistentAgent_ResultInvalid()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = BuildCommand(99999, Guid.NewGuid().ToString(), "user", "Hello");

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert — validator runs before handler; non-existent agent returns InvalidResult (400), not NotFound
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AgentMustExist);
        }

        [Fact]
        public async Task StoreConversation_WithNoTenant_ResultInvalid()
        {
            // Arrange
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = BuildCommand(1, Guid.NewGuid().ToString(), "user", "Hello");

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid);
        }

        private static StoreConversationTenantAgentByIdCommand BuildCommand(int agentId, string sessionId, string role, string text)
        {
            return new StoreConversationTenantAgentByIdCommand
            {
                Id = agentId,
                SessionId = sessionId,
                Messages = new List<StoreConversationMessageInput>
                {
                    new StoreConversationMessageInput { Role = role, Text = text }
                }
            };
        }
    }
}
