using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using System.Collections.Generic;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>WI 7575: a blocked tool must not leak into the prompt (capabilities list, PriorityQA, ContactForm).</summary>
    public class SemanticKernelAgentServiceBlockedToolInjectionTests
    {
        private static TenantAgent Agent(string weatherPermission) => new()
        {
            Id = 1,
            AgentTools = new List<AgentTool>
            {
                new() { AgentId = 1, ToolId = 1, ToolPermission = weatherPermission, Tool = new TenantTool { Id = 1, Name = "Weather lookup", Description = "Looks up the weather", Type = "HTTP", IsActive = true, PermissionsEnabled = true } },
                new() { AgentId = 1, ToolId = 2, ToolPermission = "Allow", Tool = new TenantTool { Id = 2, Name = "Calendar lookup", Description = "Reads the calendar", Type = "HTTP", IsActive = true, PermissionsEnabled = true } }
            }
        };

        [Fact]
        public void CapabilitiesInjection_SkipsBlockedTool()
        {
            var agent = Agent("Block");

            var text = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, new ChatContext(), ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            Assert.DoesNotContain("Weather lookup", text);
            Assert.Contains("Calendar lookup", text);
        }

        [Fact]
        public void CapabilitiesInjection_AskToolStaysListed_WhenApprovalsAvailable()
        {
            var agent = Agent("Ask");

            var text = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, new ChatContext(), ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            Assert.Contains("Weather lookup", text);
        }

        [Theory]
        [InlineData("Block", true, true)]
        [InlineData("Ask", false, true)]
        [InlineData("Ask", true, false)]
        [InlineData(null, false, true)]
        [InlineData(null, true, true)]
        [InlineData("Allow", false, false)]
        public void IsBlocked_FollowsPolicy(string permission, bool approvalsAvailable, bool expected)
        {
            var agent = Agent(permission);
            var tool = (RunableTool)System.Linq.Enumerable.First(agent.AgentTools).Tool;

            Assert.Equal(expected, SemanticKernelAgentService.IsBlocked(ToolPermissionPolicy.Build(agent, approvalsAvailable), tool));
            Assert.False(SemanticKernelAgentService.IsBlocked(null, tool));
        }
    }
}
