using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.GetAvailableSpinUpAgentsByTenantAgentId;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.GetAvailableSpinUpAgentsByTenantAgentId
{
    public class GetAvailableSpinUpAgentsByTenantAgentIdQueryHandlerTests
    {
        private const string TenantName = "test-tenant";

        private static GetAvailableSpinUpAgentsByTenantAgentIdQueryHandler BuildHandler(
            IEnumerable<Agent> agents,
            IEnumerable<AgentAllowedSpinUpAgent> allowedSpinUpAgents = null,
            IEnumerable<AgentChatSetting> chatSettings = null,
            string tenantName = TenantName)
        {
            var dbSet = new List<Agent>(agents).BuildMockDbSet();
            var allowedDbSet = new List<AgentAllowedSpinUpAgent>(allowedSpinUpAgents ?? []).BuildMockDbSet();
            var chatSettingsDbSet = new List<AgentChatSetting>(chatSettings ?? []).BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(dbSet.Object);
            db.Setup(x => x.AgentAllowedSpinUpAgents).Returns(allowedDbSet.Object);
            db.Setup(x => x.AgentChatSettings).Returns(chatSettingsDbSet.Object);

            var tenantContext = new TenantContext { TenantName = tenantName };

            return new GetAvailableSpinUpAgentsByTenantAgentIdQueryHandler(db.Object, tenantContext);
        }

        private static Tenant BuildTenant() => new() { Id = 1, Name = TenantName };

        private static AgentAllowedSpinUpAgent Allow(int agentId, int allowedSpinUpAgentId) =>
            new() { AgentId = agentId, AllowedSpinUpAgentId = allowedSpinUpAgentId };

        [Fact]
        public async Task Handle_ExistingAgentWithAllowlistedTargets_ReturnsSortedAllowedNames()
        {
            var tenant = BuildTenant();
            var handler = BuildHandler(
                new Agent[]
                {
                    new TenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true },
                    new PublishedTenantAgent { Id = 2, Name = "Zeta Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true },
                    new TenantAgent { Id = 3, Name = "Alpha Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                [Allow(1, 2), Allow(1, 3)]);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = 1 }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            Assert.Equal(new[] { "Alpha Bot", "Zeta Bot" }, success.Data.Select(t => t.Name));
        }

        [Fact]
        public async Task Handle_TargetWithConfiguredIcon_ReturnsIconUrl()
        {
            var tenant = BuildTenant();
            var handler = BuildHandler(
                new Agent[]
                {
                    new TenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true },
                    new TenantAgent { Id = 2, Name = "Iconed Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                [Allow(1, 2)],
                [new AgentChatSetting { Id = 1, AgentId = 2, ChatbotIconPath = "https://cdn.test/iconed-bot.png" }]);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = 1 }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            var target = Assert.Single(success.Data);
            Assert.Equal("Iconed Bot", target.Name);
            Assert.Equal("https://cdn.test/iconed-bot.png", target.IconUrl);
        }

        [Fact]
        public async Task Handle_TargetWithoutConfiguredIcon_ReturnsNullIconUrl()
        {
            var tenant = BuildTenant();
            var handler = BuildHandler(
                new Agent[]
                {
                    new TenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true },
                    new TenantAgent { Id = 2, Name = "Iconless Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                [Allow(1, 2)]);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = 1 }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            var target = Assert.Single(success.Data);
            Assert.Equal("Iconless Bot", target.Name);
            Assert.Null(target.IconUrl);
        }

        [Fact]
        public async Task Handle_NoAllowlistEntries_ReturnsEmptyListEvenWithOtherActiveAgents()
        {
            var tenant = BuildTenant();
            var handler = BuildHandler(new Agent[]
            {
                new TenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true },
                new TenantAgent { Id = 2, Name = "Other Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
            });

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = 1 }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            Assert.Empty(success.Data);
        }

        [Fact]
        public async Task Handle_NonExistentId_ReturnsNotFound()
        {
            var tenant = BuildTenant();
            var handler = BuildHandler(new Agent[]
            {
                new TenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
            });

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = 999 }, TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Handle_SoftDeletedCurrentAgent_ReturnsNotFound()
        {
            var tenant = BuildTenant();
            var handler = BuildHandler(new Agent[]
            {
                new TenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true, IsDeleted = true }
            });

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = 1 }, TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Handle_CurrentAgentIsMarketplaceAgent_ReturnsNotFound()
        {
            var tenant = BuildTenant();
            var handler = BuildHandler(new Agent[]
            {
                new MarketplaceAgent { Id = 1, Name = "Marketplace Bot", Tenant = tenant, TenantId = tenant.Id }
            });

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = 1 }, TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Handle_AllowlistedTargetsExcludeInactiveDeletedAndOthersAgentAllowlist()
        {
            var tenant = BuildTenant();

            // Every one of these targets is allowlisted for agent 1, but only "Valid Bot" should be
            // returned — inactive/deleted targets are filtered out even if a stale allowlist row exists,
            // and agent 7's own allowlist entry (for agent 2) must not leak into agent 1's list.
            var handler = BuildHandler(
                new Agent[]
                {
                    new TenantAgent { Id = 1, Name = "Self", Tenant = tenant, TenantId = tenant.Id, IsActive = true },
                    new TenantAgent { Id = 2, Name = "Inactive Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = false },
                    new TenantAgent { Id = 3, Name = "Deleted Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true, IsDeleted = true },
                    new MarketplaceAgent { Id = 5, Name = "Marketplace Bot", Tenant = tenant, TenantId = tenant.Id },
                    new TenantAgent { Id = 6, Name = "Valid Bot", Tenant = tenant, TenantId = tenant.Id, IsActive = true }
                },
                [Allow(1, 2), Allow(1, 3), Allow(1, 5), Allow(1, 6), Allow(7, 2)]);

            var result = await handler.Handle(new GetAvailableSpinUpAgentsByTenantAgentIdQuery { Id = 1 }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<AgentSpinUpTargetResult>>>(result);
            Assert.Equal(new[] { "Valid Bot" }, success.Data.Select(t => t.Name));
        }
    }
}
