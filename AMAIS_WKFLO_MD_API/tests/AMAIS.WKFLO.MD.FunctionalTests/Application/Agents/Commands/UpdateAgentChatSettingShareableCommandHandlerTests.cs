using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateAgentChatSettingShareable;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Commands
{
    public class UpdateAgentChatSettingShareableCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>, IDisposable
    {
        private TestFixture Fixture => fixture;

        public void Dispose()
        {
        }

        [Fact]
        public async Task UpdateAgentChatSettingShareable_CreatesNewSetting_DefaultsChatWidth()
        {
            // Regression test for Azure DevOps bug 6696: UpdateAgentChatSettingShareableCommand
            // only carries Id/IsWidget/IsShareable, so Mapster leaves ChatWidth (and the other
            // style fields) at their C# default (null) when creating a brand-new AgentChatSetting.
            // ChatWidth is NOT NULL in SQL Server with a 'standard' default, so this previously
            // caused SaveChangesAsync to fail with a null constraint violation in production.

            // Arrange
            Fixture.DeleteAgentChatSettingData();
            Fixture.AddTenantContext("Test");
            var db = Fixture.GetDbContext();
            var tenant = db.Tenants.First(m => m.Name == "Test");

            var tenantAgent = new TenantAgent
            {
                Name = "Agent",
                Description = "Agent Description",
                SystemPrompt = "Agent Prompt",
                IsActive = true,
                TenantId = tenant.Id
            };

            var publishedAgent = new PublishedTenantAgent
            {
                Name = "Published Agent",
                Description = "Published Description",
                SystemPrompt = "Published Prompt",
                IsActive = true,
                TenantId = tenant.Id,
                CreatedByTenantAgent = tenantAgent
            };

            tenantAgent.PublishedTenantAgent = publishedAgent;

            db.TenantAgents.Add(tenantAgent);
            db.PublishedTenantAgents.Add(publishedAgent);
            db.SaveChanges();

            var command = new UpdateAgentChatSettingShareableCommand
            {
                Id = tenantAgent.Id,
                IsWidget = false,
                IsShareable = true
            };

            var mediator = Fixture.GetMediator();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);

            var storedSetting = db.AgentChatSettings
                .Single(setting => setting.AgentId == publishedAgent.Id && setting.IsWidget == false);

            Assert.Equal(AgentChatSettingDefaults.ChatWidth, storedSetting.ChatWidth);
            Assert.Equal(AgentChatSettingDefaults.PrimaryColor, storedSetting.PrimaryColor);
            Assert.Equal(AgentChatSettingDefaults.BubbleStyle, storedSetting.BubbleStyle);
            Assert.Equal(AgentChatSettingDefaults.FloatingPosition, storedSetting.FloatingPosition);
            Assert.Equal(AgentChatSettingDefaults.LauncherStyle, storedSetting.LauncherStyle);
            Assert.True(storedSetting.ShowDescription);
            Assert.False(storedSetting.ChainOfThought);
            Assert.True(storedSetting.IsShareable);
            Assert.False(string.IsNullOrEmpty(storedSetting.ShareId));
        }

        [Fact]
        public async Task UpdateAgentChatSettingShareable_ExistingSetting_DoesNotOverwriteStoredStyleFields()
        {
            // Arrange
            Fixture.DeleteAgentChatSettingData();
            Fixture.AddTenantContext("Test");
            var db = Fixture.GetDbContext();
            var tenant = db.Tenants.First(m => m.Name == "Test");

            var tenantAgent = new TenantAgent
            {
                Name = "Agent",
                Description = "Agent Description",
                SystemPrompt = "Agent Prompt",
                IsActive = true,
                TenantId = tenant.Id
            };

            var publishedAgent = new PublishedTenantAgent
            {
                Name = "Published Agent",
                Description = "Published Description",
                SystemPrompt = "Published Prompt",
                IsActive = true,
                TenantId = tenant.Id,
                CreatedByTenantAgent = tenantAgent
            };

            tenantAgent.PublishedTenantAgent = publishedAgent;

            db.TenantAgents.Add(tenantAgent);
            db.PublishedTenantAgents.Add(publishedAgent);
            db.SaveChanges();

            var existingSetting = new AgentChatSetting
            {
                AgentId = publishedAgent.Id,
                IsWidget = false,
                ChatWidth = "wide",
                PrimaryColor = "#000000",
                ShowDescription = false
            };

            db.AgentChatSettings.Add(existingSetting);
            db.SaveChanges();

            var command = new UpdateAgentChatSettingShareableCommand
            {
                Id = tenantAgent.Id,
                IsWidget = false,
                IsShareable = true
            };

            var mediator = Fixture.GetMediator();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);

            var updatedSetting = db.AgentChatSettings.Single(setting => setting.Id == existingSetting.Id);

            Assert.Equal("wide", updatedSetting.ChatWidth);
            Assert.Equal("#000000", updatedSetting.PrimaryColor);
            Assert.False(updatedSetting.ShowDescription);
            Assert.True(updatedSetting.IsShareable);
        }
    }
}
