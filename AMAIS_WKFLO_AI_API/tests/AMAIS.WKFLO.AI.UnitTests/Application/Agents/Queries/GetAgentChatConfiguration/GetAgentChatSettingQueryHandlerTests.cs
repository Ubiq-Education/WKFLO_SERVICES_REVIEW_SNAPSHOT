using AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatConfiguration;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.GetAgentChatConfiguration
{
    public class GetAgentChatSettingQueryHandlerTests
    {
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly IWkfloMapper _mapper;
        private readonly GetPublicAgentChatSettingQueryHandler _handler;

        public GetAgentChatSettingQueryHandlerTests()
        {
            _mapper = WkfloMapperExtensions.CreateMapper();

            _dbMock = new Mock<IMasterDataDbContext>();
            _dbMock.Setup(x => x.EntityTags).Returns(new List<EntityTag>().BuildMockDbSet().Object);
            _dbMock.Setup(x => x.TenantSettings).Returns(new List<TenantSetting>().BuildMockDbSet().Object);

            _handler = new GetPublicAgentChatSettingQueryHandler(_dbMock.Object, _mapper);
        }

        // US 7974: the widget and share pages get the agent's tenant's date/time display settings with the configuration.

        private AgentChatSetting SetupShareableAgent(int tenantId)
        {
            var tenant = new Tenant { Id = tenantId, Name = "TestTenant", Region = "US" };
            var agentChatSetting = new AgentChatSetting
            {
                ShareId = Guid.NewGuid().ToString(),
                IsShareable = true,
                Agent = new PublishedTenantAgent { Id = 7, Name = "Agent", AgentTools = new List<AgentTool>(), TenantId = tenantId, Tenant = tenant }
            };
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { agentChatSetting }.BuildMockDbSet().Object);
            return agentChatSetting;
        }

        [Fact]
        public async Task Handle_TenantWithSavedSettings_ReturnsItsDisplaySettings()
        {
            var agentChatSetting = SetupShareableAgent(tenantId: 3);
            _dbMock.Setup(x => x.TenantSettings).Returns(new List<TenantSetting>
            {
                new() { Id = 1, TenantId = 4, TimeZone = "Asia/Tokyo", TimeFormat = "24h", DateFormat = "yyyy-MM-dd" },
                new() { Id = 2, TenantId = 3, TimeZone = "Europe/London", TimeFormat = "12h", DateFormat = "dd/MM/yyyy" }
            }.BuildMockDbSet().Object);

            var result = await _handler.Handle(new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId }, TestContext.Current.CancellationToken);

            var displaySettings = Assert.IsType<SuccessResult<AgentChatSettingResult>>(result).Data.DisplaySettings;
            Assert.Equal("Europe/London", displaySettings.TimeZone);
            Assert.Equal("12h", displaySettings.TimeFormat);
            Assert.Equal("dd/MM/yyyy", displaySettings.DateFormat);
        }

        [Fact]
        public async Task Handle_TenantWithoutSettings_ReturnsTheDefaultDisplaySettings()
        {
            var agentChatSetting = SetupShareableAgent(tenantId: 3);
            _dbMock.Setup(x => x.TenantSettings).Returns(new List<TenantSetting>
            {
                new() { Id = 2, TenantId = 3, TimeZone = "Europe/London", TimeFormat = "12h", DateFormat = "dd/MM/yyyy", IsDeleted = true }
            }.BuildMockDbSet().Object);

            var result = await _handler.Handle(new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId }, TestContext.Current.CancellationToken);

            var displaySettings = Assert.IsType<SuccessResult<AgentChatSettingResult>>(result).Data.DisplaySettings;
            Assert.Equal("UTC", displaySettings.TimeZone);
            Assert.Equal("24h", displaySettings.TimeFormat);
            Assert.Equal("yyyy-MM-dd", displaySettings.DateFormat);
        }

        [Fact]
        public async Task Handle_WithValidTenantAgent_ReturnsSuccessResult()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = "TestTenant", Region = "US" };
            var publishedAgent = new PublishedTenantAgent
            {
                Id = 1,
                Name = "Test Agent",
                Description = "Test Description",
                AgentTools = new List<AgentTool>()
                {
                    new AgentTool()
                    {
                        Tool = new PublishedTenantTool()
                        {
                            Id = 1,
                            Name = "Test Tool",
                            Description = "Test Tool Description",
                            Type = CommonToolTypeConsts.IntegrationAmais,
                            IsActive = true,
                            IsDeleted = false,
                            Tenant = tenant
                        }
                    }
                },
                IsDeleted = false,
                IsActive = true,
                Tenant = tenant
            };

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#3b82f6",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat",
                IsShareable = true,
                SecurityConfiguration = JsonConvert.SerializeObject(new AgentSecurityConfiguration()
                {
                    Type = "azure-ad",
                    AzureAd = new AzureAdConfiguration
                    {
                        ClientId = "client-id",
                        TenantId = "tenant-id",
                        Scopes = new[] { "scope1", "scope2" }
                    }
                }),
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id,
                Agent = publishedAgent
            };

            var agentChatSettings = new List<AgentChatSetting>() { agentChatSetting }.BuildMockDbSet();

            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingResult>>(result);
            var successResult = (SuccessResult<AgentChatSettingResult>)result;

            // Verify configuration properties (Name and Description removed from AgentChatSettingResult)
            Assert.Equal("#3b82f6", successResult.Data.PrimaryColor);
            Assert.Equal("rounded", successResult.Data.BubbleStyle);
            Assert.True(successResult.Data.ShowDescription);
            Assert.False(successResult.Data.IsWidget);
            Assert.Equal("https://api.example.com/chat", successResult.Data.ApiEndpoint);
            Assert.True(successResult.Data.IsShareable);
            Assert.Equal(publishedAgent.Id, successResult.Data.AgentId);

            // Verify security configuration
            Assert.Equal("azure-ad", successResult.Data.SecurityConfiguration.Type);
            Assert.NotNull(successResult.Data.SecurityConfiguration.AzureAd);
            Assert.Equal("client-id", successResult.Data.SecurityConfiguration.AzureAd.ClientId);
            Assert.Equal("tenant-id", successResult.Data.SecurityConfiguration.AzureAd.TenantId);
            Assert.Equal(new[] { "scope1", "scope2" }, successResult.Data.SecurityConfiguration.AzureAd.Scopes);
        }

        [Fact]
        public async Task Handle_WithValidPublishedAgent_ReturnsSuccessResult()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = "PublicTenant", Region = "US" };
            var publishedAgent = new PublishedTenantAgent
            {
                Id = 2,
                Name = "Published Agent",
                Description = "Published Description",
                AgentTools = new List<AgentTool>()
                {
                    new AgentTool()
                    {
                        Tool = new PublishedTenantTool()
                        {
                            Id = 1,
                            Name = "Test Tool",
                            Description = "Test Tool Description",
                            Type = CommonToolTypeConsts.IntegrationAmais,
                            IsActive = true,
                            IsDeleted = false,
                            Tenant = tenant
                        }
                    }
                },
                IsDeleted = false,
                IsActive = true,
                Tenant = tenant
            };

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#28a745",
                BubbleStyle = "square",
                ShowDescription = false,
                IsWidget = true,
                ApiEndpoint = "https://api.example.com/chat/public",
                IsShareable = false,
                SecurityConfiguration = JsonConvert.SerializeObject(new AgentSecurityConfiguration()
                {
                    Type = "none"
                }),
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id,
                Agent = publishedAgent
            };

            var agentChatSettings = new List<AgentChatSetting>() { agentChatSetting }.BuildMockDbSet();

            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingResult>>(result);
            var successResult = (SuccessResult<AgentChatSettingResult>)result;

            // Verify configuration properties (Name and Description removed from AgentChatSettingResult)
            Assert.Equal("#28a745", successResult.Data.PrimaryColor);
            Assert.Equal("square", successResult.Data.BubbleStyle);
            Assert.False(successResult.Data.ShowDescription);
            Assert.True(successResult.Data.IsWidget);
            Assert.Equal("https://api.example.com/chat/public", successResult.Data.ApiEndpoint);
            Assert.False(successResult.Data.IsShareable);
            Assert.Equal(publishedAgent.Id, successResult.Data.AgentId);

            // Verify security configuration
            Assert.Equal("none", successResult.Data.SecurityConfiguration.Type);
        }

        [Fact]
        public async Task Handle_WithPublicTenant_ReturnsPublicSecurity()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = "PublicDemo", Region = "US" };
            var publishedAgent = new PublishedTenantAgent
            {
                Id = 1,
                Name = "Public Agent",
                Description = "Public Description",
                AgentTools = new List<AgentTool>()
                {
                    new AgentTool()
                    {
                        Tool = new PublishedTenantTool()
                        {
                            Id = 1,
                            Name = "Test Tool",
                            Description = "Test Tool Description",
                            Type = CommonToolTypeConsts.IntegrationAmais,
                            IsActive = true,
                            IsDeleted = false,
                            Tenant = tenant
                        }
                    }
                },
                IsDeleted = false,
                IsActive = true,
                Tenant = tenant
            };

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#3b82f6",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat",
                IsShareable = true,
                SecurityConfiguration = JsonConvert.SerializeObject(new AgentSecurityConfiguration()
                {
                    Type = "none"
                }),
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id,
                Agent = publishedAgent
            };

            var agentChatSettings = new List<AgentChatSetting>() { agentChatSetting }.BuildMockDbSet();

            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingResult>>(result);
            var successResult = (SuccessResult<AgentChatSettingResult>)result;

            // Verify security and configuration properties
            Assert.Equal("none", successResult.Data.SecurityConfiguration.Type);
            Assert.Equal("#3b82f6", successResult.Data.PrimaryColor);
            Assert.Equal("rounded", successResult.Data.BubbleStyle);
            Assert.True(successResult.Data.ShowDescription);
            Assert.False(successResult.Data.IsWidget);
            Assert.Equal("https://api.example.com/chat", successResult.Data.ApiEndpoint);
            Assert.True(successResult.Data.IsShareable);
            Assert.Equal(publishedAgent.Id, successResult.Data.AgentId);
        }

        [Fact]
        public async Task Handle_WithNullSecurityConfiguration_ReturnsPublicSecurity()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = "TestTenant", Region = "US" };
            var publishedAgent = new PublishedTenantAgent
            {
                Id = 1,
                Name = "Test Agent",
                Description = "Test Description",
                AgentTools = new List<AgentTool>()
                {
                    new AgentTool()
                    {
                        Tool = new PublishedTenantTool()
                        {
                            Id = 1,
                            Name = "Test Tool",
                            Description = "Test Tool Description",
                            Type = CommonToolTypeConsts.IntegrationAmais,
                            IsActive = true,
                            IsDeleted = false,
                            Tenant = tenant
                        }
                    }
                },
                IsDeleted = false,
                IsActive = true,
                Tenant = tenant
            };

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#ff6b6b",
                BubbleStyle = "circle",
                ShowDescription = true,
                IsWidget = true,
                ApiEndpoint = "https://api.example.com/chat/null-security",
                IsShareable = false,
                SecurityConfiguration = null, // Null security configuration
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id,
                Agent = publishedAgent
            };

            var agentChatSettings = new List<AgentChatSetting>() { agentChatSetting }.BuildMockDbSet();

            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingResult>>(result);
            var successResult = (SuccessResult<AgentChatSettingResult>)result;

            // Verify that null security configuration defaults to public
            Assert.Equal("none", successResult.Data.SecurityConfiguration.Type);
            Assert.Null(successResult.Data.SecurityConfiguration.AzureAd);
            Assert.Null(successResult.Data.SecurityConfiguration.ApiKey);

            // Verify other properties
            Assert.Equal("#ff6b6b", successResult.Data.PrimaryColor);
            Assert.Equal("circle", successResult.Data.BubbleStyle);
            Assert.True(successResult.Data.ShowDescription);
            Assert.True(successResult.Data.IsWidget);
            Assert.Equal("https://api.example.com/chat/null-security", successResult.Data.ApiEndpoint);
            Assert.False(successResult.Data.IsShareable);
        }

        [Fact]
        public async Task Handle_WithApiKeyConfiguration_ReturnsApiKeySecurity()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = "TestTenant", Region = "US" };
            var publishedAgent = new PublishedTenantAgent
            {
                Id = 1,
                Name = "Test Agent",
                Description = "Test Description",
                AgentTools = new List<AgentTool>()
                {
                    new AgentTool()
                    {
                        Tool = new PublishedTenantTool()
                        {
                            Id = 1,
                            Name = "Test Tool",
                            Description = "Test Tool Description",
                            Type = CommonToolTypeConsts.IntegrationAmais,
                            IsActive = true,
                            IsDeleted = false,
                            Tenant = tenant
                        }
                    }
                },
                IsDeleted = false,
                IsActive = true,
                Tenant = tenant
            };

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#ffc107",
                BubbleStyle = "rounded",
                ShowDescription = false,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat/apikey",
                IsShareable = true,
                SecurityConfiguration = JsonConvert.SerializeObject(new AgentSecurityConfiguration()
                {
                    Type = "apikey",
                    ApiKey = new ApiKeyConfiguration
                    {
                        Value = "test-api-key",
                        TenantName = "TestTenant",
                        TenantDomains = ["test.example.com"]
                    }
                }),
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id,
                Agent = publishedAgent
            };

            var agentChatSettings = new List<AgentChatSetting>() { agentChatSetting }.BuildMockDbSet();

            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingResult>>(result);
            var successResult = (SuccessResult<AgentChatSettingResult>)result;

            // Verify API key security configuration
            Assert.Equal("apikey", successResult.Data.SecurityConfiguration.Type);
            Assert.NotNull(successResult.Data.SecurityConfiguration.ApiKey);
            Assert.Equal("test-api-key", successResult.Data.SecurityConfiguration.ApiKey.Value);
            Assert.Equal("TestTenant", successResult.Data.SecurityConfiguration.ApiKey.TenantName);
            Assert.Equal(["test.example.com"], successResult.Data.SecurityConfiguration.ApiKey.TenantDomains);
            Assert.Null(successResult.Data.SecurityConfiguration.AzureAd);

            // Verify other properties
            Assert.Equal("#ffc107", successResult.Data.PrimaryColor);
            Assert.Equal("rounded", successResult.Data.BubbleStyle);
            Assert.False(successResult.Data.ShowDescription);
            Assert.False(successResult.Data.IsWidget);
            Assert.Equal("https://api.example.com/chat/apikey", successResult.Data.ApiEndpoint);
            Assert.True(successResult.Data.IsShareable);
        }

        [Fact]
        public async Task Handle_WithNonExistentAgent_ReturnsNotFoundResult()
        {
            // Arrange
            var agentChatSettings = new List<AgentChatSetting>().BuildMockDbSet();

            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = "any string" };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Handle_WithDeletedAgentChatSetting_ReturnsNotFoundResult()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = "TestTenant", Region = "US" };
            var publishedAgent = new PublishedTenantAgent
            {
                Id = 1,
                Name = "Test Agent",
                Description = "Test Description",
                IsDeleted = false,
                IsActive = true,
                Tenant = tenant
            };

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#dc3545",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat/deleted",
                IsShareable = false,
                SecurityConfiguration = null,
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id,
                IsDeleted = true // Deleted setting
            };

            var agentChatSettings = new List<AgentChatSetting>() { agentChatSetting }.BuildMockDbSet();

            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Handle_WithAgentHavingMaisyTag_ReturnsCanSelectFeatureTrue()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = "TestTenant", Region = "US" };
            var publishedAgent = new PublishedTenantAgent
            {
                Id = 1,
                Name = "Test Agent",
                Description = "Test Description",
                IsDeleted = false,
                IsActive = true,
                Tenant = tenant
            };

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#3b82f6",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat",
                IsShareable = true,
                SecurityConfiguration = null,
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id,
                Agent = publishedAgent
            };

            var agentChatSettings = new List<AgentChatSetting>() { agentChatSetting }.BuildMockDbSet();
            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            var entityTag = new EntityTag
            {
                TagId = 1,
                EntityType = TaggableEntityTypes.Agent,
                EntityId = publishedAgent.Id,
                Tag = new Tag { Id = 1, Name = FeatureTagConsts.Maisy, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.EntityTags).Returns(new List<EntityTag>() { entityTag }.BuildMockDbSet().Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingResult>>(result);
            var successResult = (SuccessResult<AgentChatSettingResult>)result;
            Assert.True(successResult.Data.CanSelectFeature);
            Assert.True(successResult.Data.IsMaisyAgent);
        }

        [Fact]
        public async Task Handle_WithAgentNotHavingMaisyTag_ReturnsCanSelectFeatureFalse()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = "TestTenant", Region = "US" };
            var publishedAgent = new PublishedTenantAgent
            {
                Id = 2,
                Name = "Test Agent",
                Description = "Test Description",
                IsDeleted = false,
                IsActive = true,
                Tenant = tenant
            };

            var agentChatSetting = new AgentChatSetting
            {
                PrimaryColor = "#3b82f6",
                BubbleStyle = "rounded",
                ShowDescription = true,
                IsWidget = false,
                ApiEndpoint = "https://api.example.com/chat",
                IsShareable = true,
                SecurityConfiguration = null,
                ShareId = Guid.NewGuid().ToString(),
                AgentId = publishedAgent.Id,
                Agent = publishedAgent
            };

            var agentChatSettings = new List<AgentChatSetting>() { agentChatSetting }.BuildMockDbSet();
            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);

            // Another agent has the Maisy tag, but not this one
            var entityTag = new EntityTag
            {
                TagId = 1,
                EntityType = TaggableEntityTypes.Agent,
                EntityId = publishedAgent.Id + 1,
                Tag = new Tag { Id = 1, Name = FeatureTagConsts.Maisy, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.EntityTags).Returns(new List<EntityTag>() { entityTag }.BuildMockDbSet().Object);

            var query = new GetPublicAgentChatSettingQuery { ShareId = agentChatSetting.ShareId };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<AgentChatSettingResult>>(result);
            var successResult = (SuccessResult<AgentChatSettingResult>)result;
            Assert.False(successResult.Data.CanSelectFeature);
            Assert.False(successResult.Data.IsMaisyAgent);
        }
    }
}