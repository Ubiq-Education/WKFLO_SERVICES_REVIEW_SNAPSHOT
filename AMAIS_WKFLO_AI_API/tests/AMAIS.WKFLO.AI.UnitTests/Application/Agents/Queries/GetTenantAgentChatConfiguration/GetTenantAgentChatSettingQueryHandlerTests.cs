using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.GetTenantAgentChatConfiguration;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.GetTenantAgentChatConfiguration
{
    public class GetTenantAgentChatSettingQueryHandlerTests
    {
        private const string TenantName = "TestTenant";

        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly IWkfloMapper _mapper;
        private readonly GetTenantAgentChatSettingQueryHandler _handler;

        public GetTenantAgentChatSettingQueryHandlerTests()
        {
            _mapper = WkfloMapperExtensions.CreateMapper();

            _dbMock = new Mock<IMasterDataDbContext>();
            _dbMock.Setup(x => x.EntityTags).Returns(new List<EntityTag>().BuildMockDbSet().Object);
            _dbMock.Setup(x => x.TenantSettings).Returns(new List<TenantSetting>().BuildMockDbSet().Object);

            var tenantContext = new TenantContext { TenantName = TenantName };

            _handler = new GetTenantAgentChatSettingQueryHandler(_dbMock.Object, _mapper, tenantContext);
        }

        [Fact]
        public async Task Handle_WithAgentHavingMaisyTag_ReturnsCanSelectFeatureTrue()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = TenantName, Region = "US" };
            var tenantAgent = new TenantAgent
            {
                Id = 1,
                Name = "Test Agent",
                Description = "Test Description",
                AgentTools = new List<AgentTool>(),
                IsActive = true,
                Tenant = tenant
            };

            var tenantAgents = new List<TenantAgent>() { tenantAgent }.BuildMockDbSet();
            _dbMock.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);

            var entityTag = new EntityTag
            {
                TagId = 1,
                EntityType = TaggableEntityTypes.Agent,
                EntityId = tenantAgent.Id,
                Tag = new Tag { Id = 1, Name = FeatureTagConsts.Maisy, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.EntityTags).Returns(new List<EntityTag>() { entityTag }.BuildMockDbSet().Object);

            var query = new GetTenantAgentChatSettingQuery { Id = tenantAgent.Id };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentChatSettingResult>>(result);
            var successResult = (SuccessResult<TenantAgentChatSettingResult>)result;
            Assert.True(successResult.Data.CanSelectFeature);
            Assert.True(successResult.Data.IsMaisyAgent);
        }

        [Fact]
        public async Task Handle_WithAgentNotHavingMaisyTag_ReturnsCanSelectFeatureFalse()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = TenantName, Region = "US" };
            var tenantAgent = new TenantAgent
            {
                Id = 2,
                Name = "Test Agent",
                Description = "Test Description",
                AgentTools = new List<AgentTool>(),
                IsActive = true,
                Tenant = tenant
            };

            var tenantAgents = new List<TenantAgent>() { tenantAgent }.BuildMockDbSet();
            _dbMock.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);

            // No EntityTags at all for this agent
            var query = new GetTenantAgentChatSettingQuery { Id = tenantAgent.Id };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentChatSettingResult>>(result);
            var successResult = (SuccessResult<TenantAgentChatSettingResult>)result;
            Assert.False(successResult.Data.CanSelectFeature);
            Assert.False(successResult.Data.IsMaisyAgent);
        }

        [Fact]
        public async Task Handle_WithNonExistentAgent_ReturnsCanSelectFeatureFalse()
        {
            // Arrange
            var tenantAgents = new List<TenantAgent>().BuildMockDbSet();
            _dbMock.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);

            var query = new GetTenantAgentChatSettingQuery { Id = 999 };

            // Act
            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentChatSettingResult>>(result);
            var successResult = (SuccessResult<TenantAgentChatSettingResult>)result;
            Assert.False(successResult.Data.CanSelectFeature);
            Assert.False(successResult.Data.IsMaisyAgent);
        }

        // US 7974: the test chat gets the tenant's date/time display settings with the configuration.

        [Fact]
        public async Task Handle_TenantWithSavedSettings_ReturnsItsDisplaySettings()
        {
            _dbMock.Setup(x => x.TenantAgents).Returns(new List<TenantAgent>().BuildMockDbSet().Object);
            _dbMock.Setup(x => x.TenantSettings).Returns(new List<TenantSetting>
            {
                new() { Id = 1, TenantId = 2, Tenant = new Tenant { Id = 2, Name = "OtherTenant" }, TimeZone = "Asia/Tokyo", TimeFormat = "24h", DateFormat = "yyyy-MM-dd" },
                new() { Id = 2, TenantId = 1, Tenant = new Tenant { Id = 1, Name = TenantName }, TimeZone = "America/New_York", TimeFormat = "12h", DateFormat = "MM/dd/yyyy" }
            }.BuildMockDbSet().Object);

            var result = await _handler.Handle(new GetTenantAgentChatSettingQuery { Id = 1 }, TestContext.Current.CancellationToken);

            var displaySettings = Assert.IsType<SuccessResult<TenantAgentChatSettingResult>>(result).Data.DisplaySettings;
            Assert.Equal("America/New_York", displaySettings.TimeZone);
            Assert.Equal("12h", displaySettings.TimeFormat);
            Assert.Equal("MM/dd/yyyy", displaySettings.DateFormat);
        }

        [Fact]
        public async Task Handle_TenantWithoutSettings_ReturnsTheDefaultDisplaySettings()
        {
            _dbMock.Setup(x => x.TenantAgents).Returns(new List<TenantAgent>().BuildMockDbSet().Object);

            var result = await _handler.Handle(new GetTenantAgentChatSettingQuery { Id = 1 }, TestContext.Current.CancellationToken);

            var displaySettings = Assert.IsType<SuccessResult<TenantAgentChatSettingResult>>(result).Data.DisplaySettings;
            Assert.Equal("UTC", displaySettings.TimeZone);
            Assert.Equal("24h", displaySettings.TimeFormat);
            Assert.Equal("yyyy-MM-dd", displaySettings.DateFormat);
        }
    }
}
