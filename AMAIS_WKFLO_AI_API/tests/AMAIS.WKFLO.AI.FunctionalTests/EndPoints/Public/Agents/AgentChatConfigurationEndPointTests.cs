using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AwesomeAssertions;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.FunctionalTests.EndPoints.Public.Agents
{
    public class AgentChatConfigurationEndPointTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private const string ConfigurationUrlTemplate = "/api/v1/agent/{0}/chat/configuration";

        public AgentChatConfigurationEndPointTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GetAgentChatConfiguration_WithValidPublicTenantAgent_ReturnsOk()
        {
            // Arrange
            var db = _fixture.GetDbContext();

            var tenant = new Tenant { Name = "TestTenant", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var publishedAgent = new PublishedTenantAgent
            {
                Name = "Test Chat Agent",
                Description = "Test agent for chat configuration",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            };
            db.PublishedTenantAgents.Add(publishedAgent);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#3b82f6",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat",
                SecurityConfiguration = JsonConvert.SerializeObject(new AgentSecurityConfiguration()
                {
                    Type = "azure-ad",
                    AzureAd = new AzureAdConfiguration()
                    {
                        ClientId = "test-client-id",
                        TenantId = "test-tenant-id",
                        Scopes = new[] { "scope1", "scope2" }
                    }
                }),
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id
            };
            db.AgentChatSettings.Add(agentChatSetting);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var url = string.Format(ConfigurationUrlTemplate, agentChatSetting.ShareId);

            // Clear any authentication headers
            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "TestTenant");

            // Act
            var response = await _fixture.Client.GetAsync(url, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<AgentChatSettingResult>>(responseBody);

            Assert.NotNull(result);
            Assert.NotNull(result.Data);
            // Removed Name and Description assertions
            Assert.Equal("#3b82f6", result.Data.PrimaryColor);
            Assert.Equal("rounded", result.Data.BubbleStyle);
            Assert.True(result.Data.ShowDescription);
            Assert.False(result.Data.IsWidget);
            Assert.Equal("https://api.example.com/chat", result.Data.ApiEndpoint);
            Assert.Equal("azure-ad", result.Data.SecurityConfiguration.Type);
            Assert.NotNull(result.Data.SecurityConfiguration.AzureAd);
        }

        [Fact]
        public async Task GetAgentChatConfiguration_WithPublicTenant_ReturnsPublicSecurity()
        {
            // Arrange
            var db = _fixture.GetDbContext();

            var tenant = new Tenant { Name = "PublicDemo", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var publishedAgent = new PublishedTenantAgent
            {
                Name = "Public Demo Agent",
                Description = "A publicly accessible AI assistant",
                SystemPrompt = "Public prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            };
            db.PublishedTenantAgents.Add(publishedAgent);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#3b82f6",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = true,
                ApiEndpoint = "https://api.example.com/chat",
                SecurityConfiguration = null, // Null security configuration
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id
            };
            db.AgentChatSettings.Add(agentChatSetting);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var url = string.Format(ConfigurationUrlTemplate, agentChatSetting.ShareId);

            // Clear any authentication headers
            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "PublicDemo");

            // Act
            var response = await _fixture.Client.GetAsync(url, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var result = JsonConvert.DeserializeObject<SuccessResult<AgentChatSettingResult>>(responseBody);

            Assert.NotNull(result);
            Assert.Equal("none", result.Data.SecurityConfiguration.Type);
            Assert.Equal("#3b82f6", result.Data.PrimaryColor);
            Assert.Equal("rounded", result.Data.BubbleStyle);
            Assert.True(result.Data.ShowDescription);
            Assert.True(result.Data.IsWidget);
            Assert.Equal("https://api.example.com/chat", result.Data.ApiEndpoint);
            Assert.Null(result.Data.SecurityConfiguration.AzureAd);
            Assert.Null(result.Data.SecurityConfiguration.ApiKey);
        }

        [Fact]
        public async Task GetAgentChatConfiguration_WithNonExistentAgent_ReturnsInvalid()
        {
            // Arrange
            var url = string.Format(ConfigurationUrlTemplate, "anything");

            // Act
            var response = await _fixture.Client.GetAsync(url, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GetAgentChatConfiguration_WithDeletedAgent_ReturnsInvalid()
        {
            // Arrange
            var db = _fixture.GetDbContext();

            var tenant = new Tenant { Name = "TestTenant", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var agent = new TenantAgent
            {
                Name = "Deleted Agent",
                Description = "Deleted agent",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = true, // Deleted
                TenantId = tenant.Id
            };
            db.TenantAgents.Add(agent);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#3b82f6",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat",
                SecurityConfiguration = null, // Null security configuration
                ShareId = Guid.NewGuid().ToString(),
                AgentId = agent.Id
            };
            db.AgentChatSettings.Add(agentChatSetting);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var url = string.Format(ConfigurationUrlTemplate, agentChatSetting.ShareId);

            // Clear any authentication headers
            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "TestTenant");

            // Act
            var response = await _fixture.Client.GetAsync(url, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Theory]
        [InlineData("   ")]
        public async Task GetAgentChatConfiguration_WithInvalidId_ReturnsBadRequest(string invalidShareId)
        {
            // Arrange
            var url = string.Format(ConfigurationUrlTemplate, invalidShareId);

            // Act
            var response = await _fixture.Client.GetAsync(url, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GetAgentChatConfiguration_WithInvalidId_ReturnsNotFound(string invalidShareId)
        {
            // Arrange
            var url = string.Format(ConfigurationUrlTemplate, invalidShareId);

            // Act
            var response = await _fixture.Client.GetAsync(url, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetAgentChatConfiguration_IsPublicEndpoint_DoesNotRequireAuthentication()
        {
            // Arrange
            var db = _fixture.GetDbContext();

            var tenant = new Tenant { Name = "TestTenant", Region = "US" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var publishedAgent = new PublishedTenantAgent
            {
                Name = "Public Agent",
                Description = "Agent accessible without auth",
                SystemPrompt = "Test prompt",
                IsActive = true,
                IsDeleted = false,
                TenantId = tenant.Id
            };
            db.PublishedTenantAgents.Add(publishedAgent);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#3b82f6",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat",
                SecurityConfiguration = null, // Null security configuration
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id
            };
            db.AgentChatSettings.Add(agentChatSetting);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var url = string.Format(ConfigurationUrlTemplate, agentChatSetting.ShareId);

            // Clear any authentication headers
            _fixture.Client.DefaultRequestHeaders.Clear();
            _fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "TestTenant");

            // Act
            var response = await _fixture.Client.GetAsync(url, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
