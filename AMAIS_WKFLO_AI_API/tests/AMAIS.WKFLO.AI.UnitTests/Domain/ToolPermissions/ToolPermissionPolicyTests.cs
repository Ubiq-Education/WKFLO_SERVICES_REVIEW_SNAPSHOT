using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using System.Collections.Generic;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions
{
    /// <summary>WI 7447/7575: effective permission rules ("Effective permission" section of the feature plan).</summary>
    public class ToolPermissionPolicyTests
    {
        internal const int McpToolId = 1;
        internal const int HttpToolId = 2;
        internal const int PermissionsOffToolId = 3;
        internal const int McpPermissionsOffToolId = 4;

        internal static TenantAgent BuildAgent(string httpPermission = "Ask", string mcpFunctionPermissions = "{\"search\":\"Ask\",\"delete\":\"Block\",\"read\":\"Allow\"}")
        {
            var tools = new List<(TenantTool Tool, string ToolPermission, string FunctionPermissions)>
            {
                (new TenantTool { Id = McpToolId, Type = "MCP", PermissionsEnabled = true, IsActive = true, EnabledFunctions = "[\"search\",\"delete\",\"read\",\"unset\"]" }, null, mcpFunctionPermissions),
                (new TenantTool { Id = HttpToolId, Type = "HTTP", PermissionsEnabled = true, IsActive = true }, httpPermission, null),
                (new TenantTool { Id = PermissionsOffToolId, Type = "Contact_Form", PermissionsEnabled = false, IsActive = true }, "Block", null),
                (new TenantTool { Id = McpPermissionsOffToolId, Type = "MCP", PermissionsEnabled = false, IsActive = true, EnabledFunctions = "[\"read\"]" }, null, "{\"read\":\"Block\"}")
            };

            var agent = new TenantAgent { Id = 100, AgentTools = new List<AgentTool>() };
            foreach (var (tool, toolPermission, functionPermissions) in tools)
            {
                agent.AgentTools.Add(new AgentTool { AgentId = 100, ToolId = tool.Id, Tool = tool, ToolPermission = toolPermission, FunctionPermissions = functionPermissions });
            }
            return agent;
        }

        [Theory]
        [InlineData(true, "Ask")]
        [InlineData(false, "Block")]
        public void NonMcpTool_AskBecomesBlockWithoutApprover(bool approvalsAvailable, string expected)
        {
            var policy = ToolPermissionPolicy.Build(BuildAgent(), approvalsAvailable);

            Assert.Equal(expected, policy.GetToolPermission(HttpToolId));
        }

        [Theory]
        [InlineData(null, "Block")]
        [InlineData("Allow", "Allow")]
        [InlineData("Block", "Block")]
        [InlineData("garbage", "Block")]
        public void NonMcpTool_UsesSavedValue_DefaultBlock(string saved, string expected)
        {
            var policy = ToolPermissionPolicy.Build(BuildAgent(httpPermission: saved), approvalsAvailable: true);

            Assert.Equal(expected, policy.GetToolPermission(HttpToolId));
        }

        [Fact]
        public void PermissionsOff_IgnoresSavedPermission()
        {
            var policy = ToolPermissionPolicy.Build(BuildAgent(), approvalsAvailable: true);

            Assert.Equal(ToolPermissionValues.Allow, policy.GetToolPermission(PermissionsOffToolId));
        }

        [Theory]
        [InlineData("search", true, "Ask")]
        [InlineData("search", false, "Block")]
        [InlineData("delete", true, "Block")]
        [InlineData("read", true, "Allow")]
        [InlineData("unset", true, "Block")]
        [InlineData("not_enabled", true, "Block")]
        [InlineData("", true, "Block")]
        public void McpFunction_AllowlistThenAgentPermission(string function, bool approvalsAvailable, string expected)
        {
            var policy = ToolPermissionPolicy.Build(BuildAgent(), approvalsAvailable);

            Assert.Equal(expected, policy.GetMcpFunctionPermission(McpToolId, function));
        }

        // Bug 7856: "Manage permissions" off ignores the allowlist (and the saved per-function value): every function is Allow.
        [Theory]
        [InlineData("read", "Allow")]
        [InlineData("write", "Allow")]
        [InlineData("", "Block")]
        public void McpWithPermissionsOff_IgnoresAllowlist_EveryFunctionIsAllow(string function, string expected)
        {
            var policy = ToolPermissionPolicy.Build(BuildAgent(), approvalsAvailable: true);

            Assert.Equal(expected, policy.GetMcpFunctionPermission(McpPermissionsOffToolId, function));
        }

        // Bug 7856: flag OFF = every function Allow (the allowlist is ignored, even a non-empty one). Flag ON: no
        // enabled function (NULL, [] or malformed) = every function is on the allowlist, a non-empty allowlist limits
        // the tool to the listed functions, then the agent's saved per-function permission (nothing saved = Block).
        [Theory]
        [InlineData(null, false, "anything", "Allow")]
        [InlineData("[]", false, "anything", "Allow")]
        [InlineData("not json", false, "anything", "Allow")]
        [InlineData("[\"read\"]", false, "read", "Allow")]
        [InlineData("[\"read\"]", false, "write", "Allow")]
        [InlineData(null, true, "read", "Allow")]
        [InlineData(null, true, "search", "Ask")]
        [InlineData(null, true, "delete", "Block")]
        [InlineData(null, true, "unsaved", "Block")]
        [InlineData("[]", true, "read", "Allow")]
        [InlineData("[]", true, "unsaved", "Block")]
        [InlineData("[\"read\"]", true, "read", "Allow")]
        [InlineData("[\"read\"]", true, "search", "Block")]
        [InlineData(null, false, "", "Block")]
        [InlineData("[]", true, null, "Block")]
        public void McpFunction_EmptyAllowlistAllowsAllFunctions(string enabledFunctions, bool permissionsEnabled, string function, string expected)
        {
            var tool = new TenantTool { Id = 50, Type = "MCP", PermissionsEnabled = permissionsEnabled, IsActive = true, EnabledFunctions = enabledFunctions };
            var agent = new TenantAgent
            {
                Id = 100,
                AgentTools = new List<AgentTool>
                {
                    new() { AgentId = 100, ToolId = tool.Id, Tool = tool, FunctionPermissions = "{\"read\":\"Allow\",\"search\":\"Ask\",\"delete\":\"Block\"}" }
                }
            };

            var policy = ToolPermissionPolicy.Build(agent, approvalsAvailable: true);

            Assert.Equal(expected, policy.GetMcpFunctionPermission(tool.Id, function));
        }

        [Theory]
        [InlineData("plugin_2_HTTP", "anything", "Ask")]
        [InlineData("plugin_1_MCP", "delete", "Block")]
        [InlineData("plugin_1_MCP", "search", "Ask")]
        [InlineData("plugin_spin_up_agent_event_publisher", "publish_agent_event", "Allow")]
        [InlineData("plugin_999_HTTP", "x", "Allow")]
        [InlineData(null, "x", "Allow")]
        public void GetPermission_ResolvesToolFromPluginName(string pluginName, string function, string expected)
        {
            var policy = ToolPermissionPolicy.Build(BuildAgent(), approvalsAvailable: true);

            Assert.Equal(expected, policy.GetPermission(pluginName, function));
        }

        [Theory]
        [InlineData("plugin_12_Integration_Amais_Knowledge", true, 12)]
        [InlineData("plugin_7_MCP", true, 7)]
        [InlineData("plugin_spin_up", false, 0)]
        [InlineData("other_1_MCP", false, 0)]
        public void TryParseToolId_HandlesSuffixesAndNonToolPlugins(string pluginName, bool ok, int toolId)
        {
            Assert.Equal(ok, ToolPermissionPolicy.TryParseToolId(pluginName, out var parsed));
            Assert.Equal(toolId, parsed);
        }

        [Fact]
        public void AllowAll_AllowsNonMcp()
        {
            Assert.Equal(ToolPermissionValues.Allow, ToolPermissionPolicy.AllowAll.GetToolPermission(HttpToolId));
            Assert.False(ToolPermissionPolicy.AllowAll.ApprovalsAvailable);
        }
    }
}
