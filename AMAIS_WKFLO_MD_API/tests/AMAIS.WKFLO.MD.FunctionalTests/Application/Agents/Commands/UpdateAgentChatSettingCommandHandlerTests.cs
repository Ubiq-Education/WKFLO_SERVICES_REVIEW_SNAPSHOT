using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateAgentChatSetting;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Commands
{
    // Mutates the process-wide DEFAULT_WEB_ORIGINS environment variable (see below), so this
    // class shares a collection with every other test class that reads or writes it — xUnit
    // guarantees classes in the same collection never run concurrently, which is the only thing
    // that prevents that mutation from leaking into an unrelated test running in parallel.
    [Collection(DefaultWebOriginsEnvironmentCollection.Name)]
    public class UpdateAgentChatSettingCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>, IDisposable
    {
        private const string DefaultWebOriginsEnvironmentVariable = "DEFAULT_WEB_ORIGINS";
        private readonly string _originalDefaultWebOrigins = Environment.GetEnvironmentVariable("DEFAULT_WEB_ORIGINS");

        private TestFixture Fixture => fixture;

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(DefaultWebOriginsEnvironmentVariable, _originalDefaultWebOrigins);
        }

        [Fact]
        public async Task UpdateAgentChatSetting_WithPrompts_SynchronizesPrompts()
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

            var existingPrompt = new PredefinedPrompt
            {
                Title = "Welcome",
                Prompt = "Hello"
            };

            var removePrompt = new PredefinedPrompt
            {
                Title = "Old",
                Prompt = "Remove"
            };

            var existingSetting = new AgentChatSetting
            {
                AgentId = publishedAgent.Id,
                PrimaryColor = "#000000",
                BubbleStyle = "rounded",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false,
                PredefinedPrompts = new List<PredefinedPrompt> { existingPrompt, removePrompt }
            };

            existingPrompt.AgentChatSetting = existingSetting;
            removePrompt.AgentChatSetting = existingSetting;

            db.AgentChatSettings.Add(existingSetting);
            db.SaveChanges();

            var existingCreatedOn = db.PredefinedPrompts
                .Single(prompt => prompt.Title == "Welcome")
                .CreatedOn;

            var command = new UpdateAgentChatSettingCommand
            {
                Id = tenantAgent.Id,
                PrimaryColor = "#111111",
                BubbleStyle = "rounded",
                FloatingPosition = "right",
                LauncherStyle = "circle",
                ChatWidth = "standard",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false,
                PredefinedPrompts = new List<PredefinedPromptModel>
                {
                    new() { Title = "Welcome", Prompt = "Hello updated" },
                    new() { Title = "New", Prompt = "New prompt" }
                }
            };

            var mediator = Fixture.GetMediator();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);

            var updatedSetting = db.AgentChatSettings
                .Include(setting => setting.PredefinedPrompts)
                .Single(setting => setting.Id == existingSetting.Id);

            Assert.Equal(2, updatedSetting.PredefinedPrompts.Count);
            Assert.Contains(updatedSetting.PredefinedPrompts, prompt => prompt.Title == "Welcome" && prompt.Prompt == "Hello updated");
            Assert.Contains(updatedSetting.PredefinedPrompts, prompt => prompt.Title == "New" && prompt.Prompt == "New prompt");
            Assert.DoesNotContain(updatedSetting.PredefinedPrompts, prompt => prompt.Title == "Old");

            var updatedExisting = updatedSetting.PredefinedPrompts.Single(prompt => prompt.Title == "Welcome");
            Assert.Equal(existingCreatedOn, updatedExisting.CreatedOn);
        }

        [Fact]
        public async Task UpdateAgentChatSetting_CreatesNewSetting_WithOmittedFields_AppliesBusinessDefaults()
        {
            // Regression test for Azure DevOps bug 6696: creating a new AgentChatSetting via the
            // update endpoint's "create new" branch must never leave a NOT NULL column at its
            // C# default when the caller omits the field (PrimaryColor, ShowDescription, etc.).

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

            var command = new UpdateAgentChatSettingCommand
            {
                Id = tenantAgent.Id,
                // PrimaryColor and ApiEndpoint are [Required] on the command and enforced by the
                // technical (DataAnnotations) validation step in ValidatorBehavior, so they must
                // be supplied. ChatWidth, BubbleStyle, FloatingPosition, LauncherStyle and
                // ShowDescription are NOT required and are intentionally omitted here.
                PrimaryColor = "#3b82f6",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false
            };

            var mediator = Fixture.GetMediator();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);

            var storedSetting = db.AgentChatSettings.Single(setting => setting.AgentId == publishedAgent.Id);

            Assert.Equal(AgentChatSettingDefaults.ChatWidth, storedSetting.ChatWidth);
            Assert.Equal(AgentChatSettingDefaults.PrimaryColor, storedSetting.PrimaryColor);
            Assert.Equal(AgentChatSettingDefaults.BubbleStyle, storedSetting.BubbleStyle);
            Assert.Equal(AgentChatSettingDefaults.FloatingPosition, storedSetting.FloatingPosition);
            Assert.Equal(AgentChatSettingDefaults.LauncherStyle, storedSetting.LauncherStyle);
            Assert.True(storedSetting.ShowDescription);
        }

        [Fact]
        public async Task UpdateAgentChatSetting_ExistingSetting_OmittedShowDescription_PreservesStoredValue()
        {
            // A caller that omits ShowDescription on an update must not silently flip a
            // previously-true value back to false — the handler should preserve whatever is
            // already stored when the field isn't explicitly supplied.

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
                PrimaryColor = "#000000",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false,
                ShowDescription = true
            };

            db.AgentChatSettings.Add(existingSetting);
            db.SaveChanges();

            var command = new UpdateAgentChatSettingCommand
            {
                Id = tenantAgent.Id,
                PrimaryColor = "#111111",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false
                // ShowDescription intentionally omitted.
            };

            var mediator = Fixture.GetMediator();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);

            var updatedSetting = db.AgentChatSettings.Single(setting => setting.Id == existingSetting.Id);
            Assert.True(updatedSetting.ShowDescription);
        }

        [Fact]
        public async Task UpdateAgentChatSetting_CreatesNewSetting_WithJwtTokenCallbackAndDefaultWebOrigins_MergesDefaultDomains()
        {
            // Arrange
            Environment.SetEnvironmentVariable(DefaultWebOriginsEnvironmentVariable, "localhost;localhost:3001");
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

            var command = new UpdateAgentChatSettingCommand
            {
                Id = tenantAgent.Id,
                PrimaryColor = "#000000",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false,
                SecurityConfiguration = JsonConvert.SerializeObject(new AgentSecurityConfiguration
                {
                    Type = "jwt-token-callback",
                    Jwt = new JwtConfiguration { Domains = ["app.example.com"] }
                })
            };

            var mediator = Fixture.GetMediator();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);

            var storedSetting = db.AgentChatSettings.Single(setting => setting.AgentId == publishedAgent.Id);
            var securityConfiguration = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(storedSetting.SecurityConfiguration);
            Assert.Equal(["app.example.com", "localhost", "localhost:3001"], securityConfiguration.Jwt.Domains);
        }

        [Fact]
        public async Task UpdateAgentChatSetting_UpdatesExistingSetting_WithJwtTokenCallbackAndDefaultWebOrigins_MergesDefaultDomains()
        {
            // Arrange
            Environment.SetEnvironmentVariable(DefaultWebOriginsEnvironmentVariable, "localhost;localhost:3001");
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
                PrimaryColor = "#000000",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false,
                SecurityConfiguration = JsonConvert.SerializeObject(new AgentSecurityConfiguration
                {
                    Type = "none"
                })
            };

            db.AgentChatSettings.Add(existingSetting);
            db.SaveChanges();

            var command = new UpdateAgentChatSettingCommand
            {
                Id = tenantAgent.Id,
                PrimaryColor = "#111111",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false,
                SecurityConfiguration = JsonConvert.SerializeObject(new AgentSecurityConfiguration
                {
                    Type = "jwt-token-callback",
                    Jwt = new JwtConfiguration { Domains = ["app.example.com"] }
                })
            };

            var mediator = Fixture.GetMediator();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingUpdateResult>>(result);

            var updatedSetting = db.AgentChatSettings.Single(setting => setting.Id == existingSetting.Id);
            var securityConfiguration = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(updatedSetting.SecurityConfiguration);
            Assert.Equal(["app.example.com", "localhost", "localhost:3001"], securityConfiguration.Jwt.Domains);
        }
    }
}
