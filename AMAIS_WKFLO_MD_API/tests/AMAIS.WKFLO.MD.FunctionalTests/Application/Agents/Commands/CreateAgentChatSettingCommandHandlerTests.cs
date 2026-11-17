using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateAgentChatSetting;
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
    public class CreateAgentChatSettingCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>, IDisposable
    {
        private const string DefaultWebOriginsEnvironmentVariable = "DEFAULT_WEB_ORIGINS";
        private readonly string _originalDefaultWebOrigins = Environment.GetEnvironmentVariable("DEFAULT_WEB_ORIGINS");

        private TestFixture Fixture => fixture;

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(DefaultWebOriginsEnvironmentVariable, _originalDefaultWebOrigins);
        }

        [Fact]
        public async Task CreateAgentChatSetting_WithPrompts_ResultOk()
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

            db.TenantAgents.Add(tenantAgent);
            db.SaveChanges();

            var command = new CreateAgentChatSettingCommand
            {
                Id = tenantAgent.Id,
                PrimaryColor = "#000000",
                BubbleStyle = "rounded",
                FloatingPosition = "right",
                LauncherStyle = "circle",
                ChatWidth = "standard",
                ApiEndpoint = "https://api.example.com/chat",
                IsWidget = false,
                ShowDescription = true,
                ChainOfThought = false,
                PredefinedPrompts = new List<PredefinedPromptModel>
                {
                    new() { Title = "Welcome", Prompt = "Hello" },
                    new() { Title = "Help", Prompt = "How can I help?" }
                }
            };

            var mediator = Fixture.GetMediator();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingCreateResult>>(result);

            var storedSetting = db.AgentChatSettings
                .Include(setting => setting.PredefinedPrompts)
                .Single(setting => setting.AgentId == tenantAgent.Id);

            Assert.Equal(2, storedSetting.PredefinedPrompts.Count);
            Assert.Contains(storedSetting.PredefinedPrompts, prompt => prompt.Title == "Welcome" && prompt.Prompt == "Hello");
            Assert.Contains(storedSetting.PredefinedPrompts, prompt => prompt.Title == "Help" && prompt.Prompt == "How can I help?");
            Assert.All(storedSetting.PredefinedPrompts, prompt => Assert.NotEqual(default, prompt.CreatedOn));
        }

        [Fact]
        public async Task CreateAgentChatSetting_OmittedStyleAndBooleanFields_AppliesBusinessDefaults()
        {
            // Regression test for Azure DevOps bug 6696: every NOT NULL AgentChatSetting column
            // must end up with a value, and boolean business defaults (ShowDescription = true)
            // must be applied when the caller omits the field, not silently fall to C# default(bool).

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

            db.TenantAgents.Add(tenantAgent);
            db.SaveChanges();

            var command = new CreateAgentChatSettingCommand
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
            Assert.IsType<SuccessResult<AgentChatSettingCreateResult>>(result);

            var storedSetting = db.AgentChatSettings.Single(setting => setting.AgentId == tenantAgent.Id);

            Assert.Equal(AgentChatSettingDefaults.ChatWidth, storedSetting.ChatWidth);
            Assert.Equal(AgentChatSettingDefaults.PrimaryColor, storedSetting.PrimaryColor);
            Assert.Equal(AgentChatSettingDefaults.BubbleStyle, storedSetting.BubbleStyle);
            Assert.Equal(AgentChatSettingDefaults.FloatingPosition, storedSetting.FloatingPosition);
            Assert.Equal(AgentChatSettingDefaults.LauncherStyle, storedSetting.LauncherStyle);
            Assert.True(storedSetting.ShowDescription);
        }

        [Fact]
        public async Task CreateAgentChatSetting_JwtTokenCallbackWithDefaultWebOrigins_MergesDefaultDomains()
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

            db.TenantAgents.Add(tenantAgent);
            db.SaveChanges();

            var command = new CreateAgentChatSettingCommand
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
            Assert.IsType<SuccessResult<AgentChatSettingCreateResult>>(result);

            var storedSetting = db.AgentChatSettings
                .Single(setting => setting.AgentId == tenantAgent.Id);

            var securityConfiguration = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(storedSetting.SecurityConfiguration);
            Assert.Equal(["app.example.com", "localhost", "localhost:3001"], securityConfiguration.Jwt.Domains);
        }
    }
}
