using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using System.Collections.Generic;
using System.Linq;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// WI 7318 hardening: the parent agent's hand-off catalog (BuildAgentEventPublisherInjectionAsync)
    /// must only advertise allowlisted targets that are both not deleted and active.
    /// </summary>
    public class SemanticKernelAgentServiceQueryActiveAllowedSpinUpTargetsTests
    {
        private const int RootAgentId = 1;

        private static AgentAllowedSpinUpAgent Allow(int agentId, Agent target) =>
            new() { AgentId = agentId, AllowedSpinUpAgentId = target.Id, AllowedSpinUpAgent = target };

        [Fact]
        public void ExcludesInactiveDeletedAndOtherRootsTargets()
        {
            var active = new TenantAgent { Id = 10, Name = "Vera", Description = "Images", IsActive = true };
            var inactive = new TenantAgent { Id = 11, Name = "Sleepy", Description = "Off", IsActive = false };
            var deleted = new TenantAgent { Id = 12, Name = "Gone", Description = "Deleted", IsActive = true, IsDeleted = true };
            var publishedActive = new PublishedTenantAgent { Id = 13, Name = "Pub", Description = "Published", IsActive = true };
            var otherRootsTarget = new TenantAgent { Id = 14, Name = "Other", Description = "Other root", IsActive = true };

            var source = new List<AgentAllowedSpinUpAgent>
            {
                Allow(RootAgentId, active),
                Allow(RootAgentId, inactive),
                Allow(RootAgentId, deleted),
                Allow(RootAgentId, publishedActive),
                Allow(2, otherRootsTarget)
            }.AsQueryable();

            var result = SemanticKernelAgentService.QueryActiveAllowedSpinUpTargets(source, RootAgentId).ToList();

            Assert.Equal(["Vera", "Pub"], result.Select(t => t.Name));
        }

        [Fact]
        public void AllTargetsInactive_ReturnsEmpty()
        {
            var source = new List<AgentAllowedSpinUpAgent>
            {
                Allow(RootAgentId, new TenantAgent { Id = 10, Name = "Vera", IsActive = false })
            }.AsQueryable();

            var result = SemanticKernelAgentService.QueryActiveAllowedSpinUpTargets(source, RootAgentId).ToList();

            Assert.Empty(result);
        }
    }
}
