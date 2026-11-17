using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using Microsoft.SemanticKernel;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>Bug 7722: the turn resumed after a Deny/expiry must not offer the denied call again.</summary>
    public class DeniedToolResumeTests
    {
        private static ToolPermissionPolicy FreshPolicy() =>
            ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(), approvalsAvailable: true);

        private static ToolApprovalRecord McpSearch(string status, string decision)
        {
            var record = ToolApprovalHistoryTests.Record(status, decision);
            record.ToolId = ToolPermissionPolicyTests.McpToolId;
            record.IsMcp = true;
            record.PluginName = "plugin_1_MCP";
            record.FunctionName = "search";
            return record;
        }

        private static ToolApprovalRecord HttpCall(string status, string decision)
        {
            var record = ToolApprovalHistoryTests.Record(status, decision);
            record.ToolId = ToolPermissionPolicyTests.HttpToolId;
            record.IsMcp = false;
            record.PluginName = "plugin_2_HTTP";
            record.FunctionName = "call_http";
            return record;
        }

        [Theory]
        [InlineData(ToolApprovalConsts.Status.Denied, ToolApprovalConsts.Decision.Deny)]
        [InlineData(ToolApprovalConsts.Status.Expired, ToolApprovalConsts.Decision.Expired)]
        public void DeniedMcpFunction_IsBlockedInResumedTurn_OthersUnaffected(string status, string decision)
        {
            var policy = SemanticKernelAgentService.PolicyForResumedTurn(FreshPolicy(), McpSearch(status, decision));

            Assert.Equal(ToolPermissionValues.Block, policy.GetMcpFunctionPermission(ToolPermissionPolicyTests.McpToolId, "search"));
            Assert.Equal(ToolPermissionValues.Block, policy.GetPermission("plugin_1_MCP", "search"));
            // Other functions of the same MCP tool and other tools keep their permission.
            Assert.Equal(ToolPermissionValues.Allow, policy.GetMcpFunctionPermission(ToolPermissionPolicyTests.McpToolId, "read"));
            Assert.Equal(ToolPermissionValues.Ask, policy.GetToolPermission(ToolPermissionPolicyTests.HttpToolId));
            Assert.True(policy.ApprovalsAvailable);
        }

        [Fact]
        public void DeniedNonMcpTool_WholeToolIsBlockedInResumedTurn_OthersUnaffected()
        {
            var policy = SemanticKernelAgentService.PolicyForResumedTurn(FreshPolicy(), HttpCall(ToolApprovalConsts.Status.Denied, ToolApprovalConsts.Decision.Deny));

            Assert.Equal(ToolPermissionValues.Block, policy.GetToolPermission(ToolPermissionPolicyTests.HttpToolId));
            Assert.Equal(ToolPermissionValues.Block, policy.GetPermission("plugin_2_HTTP", "any_function"));
            Assert.Equal(ToolPermissionValues.Ask, policy.GetMcpFunctionPermission(ToolPermissionPolicyTests.McpToolId, "search"));
            Assert.Equal(ToolPermissionValues.Allow, policy.GetToolPermission(ToolPermissionPolicyTests.PermissionsOffToolId));
        }

        [Fact]
        public void DeniedNonMcpTool_IsNotRegisteredOrInjectedInResumedTurn()
        {
            var agent = ToolPermissionPolicyTests.BuildAgent();
            var httpTool = (TenantTool)agent.AgentTools.Single(t => t.ToolId == ToolPermissionPolicyTests.HttpToolId).Tool;
            var policy = SemanticKernelAgentService.PolicyForResumedTurn(
                ToolPermissionPolicy.Build(agent, approvalsAvailable: true), HttpCall(ToolApprovalConsts.Status.Denied, ToolApprovalConsts.Decision.Deny));

            Assert.True(SemanticKernelAgentService.IsBlocked(policy, httpTool));
            Assert.False(SemanticKernelAgentService.IsBlocked(FreshPolicy(), httpTool));
        }

        [Fact]
        public void ApprovedOrNoResume_PolicyUnchanged()
        {
            var fresh = FreshPolicy();

            Assert.Same(fresh, SemanticKernelAgentService.PolicyForResumedTurn(fresh, null));
            Assert.Same(fresh, SemanticKernelAgentService.PolicyForResumedTurn(fresh,
                McpSearch(ToolApprovalConsts.Status.Approved, ToolApprovalConsts.Decision.AllowOnce)));
        }

        [Fact]
        public void LaterTurn_AsksNormallyAgain()
        {
            var fresh = FreshPolicy();
            var resumed = SemanticKernelAgentService.PolicyForResumedTurn(fresh, McpSearch(ToolApprovalConsts.Status.Denied, ToolApprovalConsts.Decision.Deny));

            Assert.Equal(ToolPermissionValues.Block, resumed.GetMcpFunctionPermission(ToolPermissionPolicyTests.McpToolId, "search"));
            // The override is per run: the original and any newly built policy (next user turn) still Ask.
            Assert.Equal(ToolPermissionValues.Ask, fresh.GetMcpFunctionPermission(ToolPermissionPolicyTests.McpToolId, "search"));
            Assert.Equal(ToolPermissionValues.Ask, FreshPolicy().GetMcpFunctionPermission(ToolPermissionPolicyTests.McpToolId, "search"));
        }

        [Theory]
        [InlineData("plugin_1_MCP", "search", false)]
        [InlineData("plugin_1_MCP", "read", true)]
        [InlineData("plugin_2_HTTP", "call_http", true)]
        public async Task EnforcementFilter_RefusesDeniedCallInResumedTurn(string plugin, string function, bool expectedToRun)
        {
            var calls = 0;
            var policy = SemanticKernelAgentService.PolicyForResumedTurn(FreshPolicy(), McpSearch(ToolApprovalConsts.Status.Denied, ToolApprovalConsts.Decision.Deny));
            var kernel = Kernel.CreateBuilder().Build();
            kernel.FunctionInvocationFilters.Add(new ToolPermissionEnforcementFilter(policy));
            KernelFunction Fn(string name) => KernelFunctionFactory.CreateFromMethod(() => { calls++; return "ran"; }, name);
            kernel.Plugins.AddFromFunctions("plugin_2_HTTP", [Fn("call_http")]);
            kernel.Plugins.AddFromFunctions("plugin_1_MCP", [Fn("search"), Fn("read")]);

            var result = await kernel.InvokeAsync(plugin, function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(expectedToRun ? 1 : 0, calls);
            Assert.Equal(expectedToRun ? "ran" : ToolPermissionEnforcementFilter.BlockedMessage, result.ToString());
        }
    }
}
