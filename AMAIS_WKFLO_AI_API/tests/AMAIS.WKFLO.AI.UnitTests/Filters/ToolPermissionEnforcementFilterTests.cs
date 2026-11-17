using AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using Microsoft.SemanticKernel;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Filters
{
    /// <summary>WI 7447/7575: last-gate refusal of blocked / not-allowlisted calls that still reach invocation.</summary>
    public class ToolPermissionEnforcementFilterTests
    {
        private static (Kernel Kernel, Counter Counter) BuildKernel(bool approvalsAvailable)
        {
            var counter = new Counter();
            var kernel = Kernel.CreateBuilder().Build();
            kernel.FunctionInvocationFilters.Add(new ToolPermissionEnforcementFilter(ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(), approvalsAvailable)));

            KernelFunction Fn(string name) => KernelFunctionFactory.CreateFromMethod(() => { counter.Calls++; return "ran"; }, name);
            kernel.Plugins.AddFromFunctions("plugin_2_HTTP", [Fn("call_http")]);
            kernel.Plugins.AddFromFunctions("plugin_1_MCP", [Fn("search"), Fn("delete"), Fn("read"), Fn("unset"), Fn("not_enabled")]);
            kernel.Plugins.AddFromFunctions("plugin_spin_up_agent_event_publisher", [Fn("publish_agent_event")]);
            return (kernel, counter);
        }

        [Theory]
        [InlineData("plugin_1_MCP", "delete", false)]
        [InlineData("plugin_1_MCP", "not_enabled", false)]
        [InlineData("plugin_1_MCP", "unset", false)] // enabled, nothing saved => Block
        [InlineData("plugin_1_MCP", "read", true)]
        [InlineData("plugin_1_MCP", "search", true)] // Ask is the approval filter's job, not this one
        [InlineData("plugin_2_HTTP", "call_http", true)]
        [InlineData("plugin_spin_up_agent_event_publisher", "publish_agent_event", true)]
        public async Task Invoke_RunsOnlyWhenNotBlocked(string plugin, string function, bool expectedToRun)
        {
            var (kernel, counter) = BuildKernel(approvalsAvailable: true);

            var result = await kernel.InvokeAsync(plugin, function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(expectedToRun ? 1 : 0, counter.Calls);
            Assert.Equal(expectedToRun ? "ran" : ToolPermissionEnforcementFilter.BlockedMessage, result.ToString());
        }

        [Fact]
        public async Task Invoke_AskWithoutApprover_IsRefused()
        {
            var (kernel, counter) = BuildKernel(approvalsAvailable: false);

            var result = await kernel.InvokeAsync("plugin_2_HTTP", "call_http", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, counter.Calls);
            Assert.Equal(ToolPermissionEnforcementFilter.BlockedMessage, result.ToString());
        }

        private sealed class Counter
        {
            public int Calls { get; set; }
        }
    }
}
