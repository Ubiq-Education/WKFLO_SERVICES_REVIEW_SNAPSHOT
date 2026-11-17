using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.GetAvailableSpinUpAgentsByShareId;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.GetAvailableSpinUpAgentsByShareId
{
    public class GetAvailableSpinUpAgentsByShareIdQueryHandlerTests
    {
        private const string TenantName = "test-tenant";
        private const string ShareId = "share-abc";

        private static GetAvailableSpinUpAgentsByShareIdQueryHandler BuildHandler(
            IEnumerable<Agent> agents,
            IEnumerable<AgentChatSetting> chatSettings,
            IEnumerable<AgentAllowedSpinUpAgent> allowedSpinUpAgents = null)
        {
            var agentsDbSet = new List<Agent>(agents).BuildMockDbSet();
            var chatSettingsDbSet = new List<AgentChatSetting>(chatSettings).BuildMockDbSet();
            var allowedDbSet = new List<AgentAllowedSpinUpAgent>(allowedSpinUpAgents ?? []).BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(agentsDbSet.Object);
            db.Setup(x => x.AgentChatSettings).Returns(chatSettingsDbSet.Object);
            db.Setup(x => x.AgentAllowedSpinUpAgents).Returns(allowedDbSet.Object);

            return new GetAvailableSpinUpAgentsByShareIdQueryHandler(db.Object);
        }

        private static Tenant BuildTenant() => new() { Id = 1, Name = TenantName };

        private static AgentAllowedSpinUpAgent Allow(int agentId, int allowedSpinUpAgentId) =>
            new() { AgentId = agentId, AllowedSpinUpAgentId = allowedSpinUpAgentId };

        [Fact]
        public async Task Handle_ExistingShareIdWithAllowlistedTargets_ReturnsSortedAllowedNames()
        {
            var tenant = BuildTenant();
            var currentAgent = new PublishedTenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true };

            var handler = BuildHandler(
                new Agent[]
                {
                    currentAgent,
                    new TenantAgent { Id = 2, Name = "Zeta Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true },
                    new TenantAgent { Id = 3, Name = "Alpha Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                new[] { new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = 1, Agent = currentAgent } },
                [Allow(1, 2), Allow(1, 3)]);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            Assert.Equal(new[] { "Alpha Bot", "Zeta Bot" }, success.Data.Select(t => t.Name));
        }

        [Fact]
        public async Task Handle_TargetWithConfiguredIcon_ReturnsIconUrl()
        {
            var tenant = BuildTenant();
            var currentAgent = new PublishedTenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true };

            var handler = BuildHandler(
                new Agent[]
                {
                    currentAgent,
                    new TenantAgent { Id = 2, Name = "Iconed Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                new[]
                {
                    new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = 1, Agent = currentAgent },
                    new AgentChatSetting { Id = 2, AgentId = 2, ChatbotIconPath = "https://cdn.test/iconed-bot.png" }
                },
                [Allow(1, 2)]);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            var target = Assert.Single(success.Data);
            Assert.Equal("Iconed Bot", target.Name);
            Assert.Equal("https://cdn.test/iconed-bot.png", target.IconUrl);
        }

        [Fact]
        public async Task Handle_TargetWithoutConfiguredIcon_ReturnsNullIconUrl()
        {
            var tenant = BuildTenant();
            var currentAgent = new PublishedTenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true };

            var handler = BuildHandler(
                new Agent[]
                {
                    currentAgent,
                    new TenantAgent { Id = 2, Name = "Iconless Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                new[] { new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = 1, Agent = currentAgent } },
                [Allow(1, 2)]);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            var target = Assert.Single(success.Data);
            Assert.Equal("Iconless Bot", target.Name);
            Assert.Null(target.IconUrl);
        }

        [Fact]
        public async Task Handle_TargetHasWidgetAndNonWidgetIcons_MatchesRootsWidgetContext()
        {
            var tenant = BuildTenant();
            var currentAgent = new PublishedTenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true };

            var handler = BuildHandler(
                new Agent[]
                {
                    currentAgent,
                    new TenantAgent { Id = 2, Name = "Iconed Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                new[]
                {
                    // Root is shared via a non-widget AgentChatSetting.
                    new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = 1, IsWidget = false, Agent = currentAgent },
                    // Target has both a widget and a non-widget icon configured.
                    new AgentChatSetting { Id = 2, AgentId = 2, IsWidget = true, ChatbotIconPath = "https://cdn.test/widget-icon.png" },
                    new AgentChatSetting { Id = 3, AgentId = 2, IsWidget = false, ChatbotIconPath = "https://cdn.test/non-widget-icon.png" }
                },
                [Allow(1, 2)]);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            var target = Assert.Single(success.Data);
            Assert.Equal("https://cdn.test/non-widget-icon.png", target.IconUrl);
        }

        [Fact]
        public async Task Handle_NoAllowlistEntries_ReturnsEmptyListEvenWithOtherActiveAgents()
        {
            var tenant = BuildTenant();
            var currentAgent = new PublishedTenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true };

            var handler = BuildHandler(
                new Agent[]
                {
                    currentAgent,
                    new TenantAgent { Id = 2, Name = "Other Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                new[] { new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = 1, Agent = currentAgent } });

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            Assert.Empty(success.Data);
        }

        [Fact]
        public async Task Handle_NonExistentShareId_ReturnsNotFound()
        {
            var handler = BuildHandler([], []);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = "missing" }, TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Handle_SoftDeletedChatSetting_ReturnsNotFound()
        {
            var tenant = BuildTenant();
            var currentAgent = new PublishedTenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true };

            var handler = BuildHandler(
                new Agent[] { currentAgent },
                new[] { new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = 1, Agent = currentAgent, IsDeleted = true } });

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Handle_ChatSettingAgentIsMarketplaceAgent_ReturnsNotFound()
        {
            var tenant = BuildTenant();
            var currentAgent = new MarketplaceAgent { Id = 1, Name = "Marketplace Bot", Tenant = tenant, TenantId = tenant.Id };

            var handler = BuildHandler(
                new Agent[] { currentAgent },
                new[] { new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = 1, Agent = currentAgent } });

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByShareIdQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
        }
    }
}
