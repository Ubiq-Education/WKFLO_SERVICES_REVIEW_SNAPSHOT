using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Extensions
{
    /// <summary>
    /// WI 7447/7575: blocked tools / not-allowlisted MCP functions are never registered with the kernel, and the
    /// defensive enforcement filter is always added when the agent has tools.
    /// </summary>
    public class SemanticKernelExtensionsAddWkflowToolsAsyncPermissionTests
    {
        private const string McpConfig = "{\"transportType\":\"Http\",\"endpoint\":\"https://mcp.example.com/mcp\"}";

        private static Kernel BuildKernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(Mock.Of<IEisKnowledgeProvider>());
            builder.Services.AddSingleton(Mock.Of<IChatNotificationService>());
            builder.Services.AddSingleton(new TenantContext());
            builder.Services.AddSingleton(new ChatContext());
            return builder.Build();
        }

        private static TenantAgent Agent(params AgentTool[] links)
        {
            var agent = new TenantAgent { Id = 50, Name = "Agent", AgentTools = new List<AgentTool>() };
            foreach (var link in links)
            {
                link.AgentId = agent.Id;
                link.ToolId = link.Tool.Id;
                agent.AgentTools.Add(link);
            }
            return agent;
        }

        private static McpClientTool ClientTool(string name) =>
            new(Mock.Of<McpClient>(), new ModelContextProtocol.Protocol.Tool
            {
                Name = name,
                Description = name,
                InputSchema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement
            });

        private static Task<bool> InvokeAsync(Kernel kernel, Agent agent, IMcpClientService mcp, ToolPermissionPolicy policy) =>
            kernel.AddWkflowToolsAsync(agent, Mock.Of<IDatastreamProvider>(), Mock.Of<IChatNotificationService>(),
                Options.Create(new ElasticsearchSettings()), mcp, new TenantContext(), new ChatContext(), new UserContext(),
                agentTagNames: null, mapper: WkfloMapperExtensions.CreateMapper(), dateTimeProvider: Mock.Of<IDateTimeProvider>(),
                logger: NullLogger.Instance, toolPermissionPolicy: policy);

        private static Mock<IMcpClientService> Mcp(params string[] liveFunctions)
        {
            var mcp = new Mock<IMcpClientService>();
            mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            mcp.Setup(x => x.ListToolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(liveFunctions.Select(ClientTool).ToList());
            return mcp;
        }

        [Fact]
        public async Task McpTool_RegistersOnlyEnabledAndNotBlockedFunctions()
        {
            var tool = new TenantTool { Id = 1, Name = "Docs", Type = "MCP", IsActive = true, Config = McpConfig, PermissionsEnabled = true, EnabledFunctions = "[\"search\",\"delete\",\"gone\"]" };
            var agent = Agent(new AgentTool { Tool = tool, FunctionPermissions = "{\"search\":\"Allow\",\"delete\":\"Block\"}" });
            var kernel = BuildKernel();

            await InvokeAsync(kernel, agent, Mcp("search", "delete", "create").Object, ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            var plugin = Assert.Single(kernel.Plugins, p => p.Name == "plugin_1_MCP");
            Assert.Equal(new[] { "search" }, plugin.Select(f => f.Name));
            Assert.Contains(kernel.FunctionInvocationFilters, f => f is ToolPermissionEnforcementFilter);
        }

        // Bug 7856: no enabled function (NULL or []) means no allowlist, so every live function is registered.
        [Theory]
        [InlineData(null)]
        [InlineData("[]")]
        public async Task McpTool_NothingEnabled_PermissionsOff_RegistersAllFunctions(string enabledFunctions)
        {
            var tool = new TenantTool { Id = 1, Name = "Docs", Type = "MCP", IsActive = true, Config = McpConfig, PermissionsEnabled = false, EnabledFunctions = enabledFunctions };
            var agent = Agent(new AgentTool { Tool = tool });
            var kernel = BuildKernel();

            await InvokeAsync(kernel, agent, Mcp("search", "create").Object, ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            var plugin = Assert.Single(kernel.Plugins, p => p.Name == "plugin_1_MCP");
            Assert.Equal(new[] { "create", "search" }, plugin.Select(f => f.Name).OrderBy(n => n));
        }

        // Bug 7856: with permissions managed, the per-agent function permission still applies on top of the (empty) allowlist.
        [Theory]
        [InlineData(null)]
        [InlineData("[]")]
        public async Task McpTool_NothingEnabled_PermissionsOn_RegistersFunctionsNotBlockedForTheAgent(string enabledFunctions)
        {
            var tool = new TenantTool { Id = 1, Name = "Docs", Type = "MCP", IsActive = true, Config = McpConfig, PermissionsEnabled = true, EnabledFunctions = enabledFunctions };
            var agent = Agent(new AgentTool { Tool = tool, FunctionPermissions = "{\"search\":\"Allow\",\"delete\":\"Block\"}" });
            var kernel = BuildKernel();

            await InvokeAsync(kernel, agent, Mcp("search", "delete", "create").Object, ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            var plugin = Assert.Single(kernel.Plugins, p => p.Name == "plugin_1_MCP");
            Assert.Equal(new[] { "search" }, plugin.Select(f => f.Name));
        }

        [Fact]
        public async Task McpTool_NothingEnabled_PermissionsOn_NothingSaved_RegistersNoPlugin()
        {
            var tool = new TenantTool { Id = 1, Name = "Docs", Type = "MCP", IsActive = true, Config = McpConfig, PermissionsEnabled = true, EnabledFunctions = null };
            var agent = Agent(new AgentTool { Tool = tool });
            var kernel = BuildKernel();

            await InvokeAsync(kernel, agent, Mcp("search", "create").Object, ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            Assert.DoesNotContain(kernel.Plugins, p => p.Name == "plugin_1_MCP");
        }

        [Fact]
        public async Task McpTool_AskFunctionWithoutApprover_IsNotRegistered()
        {
            var tool = new TenantTool { Id = 1, Name = "Docs", Type = "MCP", IsActive = true, Config = McpConfig, PermissionsEnabled = true, EnabledFunctions = "[\"search\",\"read\"]" };
            var agent = Agent(new AgentTool { Tool = tool, FunctionPermissions = "{\"search\":\"Ask\",\"read\":\"Allow\"}" });
            var kernel = BuildKernel();

            // No policy passed: nobody can approve, so Ask acts as Block.
            await InvokeAsync(kernel, agent, Mcp("search", "read").Object, policy: null);

            Assert.Equal(new[] { "read" }, kernel.Plugins.Single(p => p.Name == "plugin_1_MCP").Select(f => f.Name));
        }

        [Theory]
        [InlineData("Block", true, false)]
        [InlineData("Ask", false, false)]
        [InlineData("Ask", true, true)]
        [InlineData(null, false, false)]
        [InlineData(null, true, false)]
        [InlineData("Allow", false, true)]
        public async Task HttpTool_WholeToolPermission(string permission, bool approvalsAvailable, bool expectedRegistered)
        {
            var tool = new TenantTool
            {
                Id = 2,
                Name = "Weather",
                Type = "HTTP",
                IsActive = true,
                PermissionsEnabled = true,
                Config = "{\"url\":\"https://api.example.com/weather\",\"method\":\"GET\"}"
            };
            var agent = Agent(new AgentTool { Tool = tool, ToolPermission = permission });
            var kernel = BuildKernel();

            await InvokeAsync(kernel, agent, Mock.Of<IMcpClientService>(), ToolPermissionPolicy.Build(agent, approvalsAvailable));

            Assert.Equal(expectedRegistered, kernel.Plugins.Any(p => p.Name == "plugin_2_HTTP"));
        }

        [Fact]
        public async Task AmaisIntegration_Blocked_RegistersNothingAndReportsSkillsDisabled()
        {
            var tool = new TenantTool { Id = 3, Name = "Integration", Type = "Integration_Amais", IsActive = true, PermissionsEnabled = true, Config = "{\"areSkillsEnabled\":true}" };
            var agent = Agent(new AgentTool { Tool = tool, ToolPermission = "Block" });
            var kernel = BuildKernel();

            var skillsEnabled = await InvokeAsync(kernel, agent, Mock.Of<IMcpClientService>(), ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            Assert.False(skillsEnabled);
            Assert.Empty(kernel.Plugins);
            Assert.Empty(kernel.Data);
        }

        [Fact]
        public async Task McpTool_EnabledFunctionWithNothingSaved_IsNotRegistered()
        {
            var tool = new TenantTool { Id = 1, Name = "Docs", Type = "MCP", IsActive = true, Config = McpConfig, PermissionsEnabled = true, EnabledFunctions = "[\"search\",\"read\"]" };
            var agent = Agent(new AgentTool { Tool = tool, FunctionPermissions = "{\"read\":\"Allow\"}" });
            var kernel = BuildKernel();

            await InvokeAsync(kernel, agent, Mcp("search", "read").Object, ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            Assert.Equal(new[] { "read" }, kernel.Plugins.Single(p => p.Name == "plugin_1_MCP").Select(f => f.Name));
        }

        // Bug 7856: "Manage permissions" off ignores a non-empty allowlist: every live function is registered.
        [Fact]
        public async Task McpTool_PermissionsOff_IgnoresAllowlist_RegistersEveryFunction()
        {
            var tool = new TenantTool { Id = 1, Name = "Docs", Type = "MCP", IsActive = true, Config = McpConfig, PermissionsEnabled = false, EnabledFunctions = "[\"search\",\"read\"]" };
            var agent = Agent(new AgentTool { Tool = tool });
            var kernel = BuildKernel();

            await InvokeAsync(kernel, agent, Mcp("search", "read", "create").Object, ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            Assert.Equal(new[] { "search", "read", "create" }, kernel.Plugins.Single(p => p.Name == "plugin_1_MCP").Select(f => f.Name));
        }

        [Fact]
        public async Task AmaisIntegration_PermissionsManaged_NothingSaved_RegistersNothing()
        {
            var tool = new TenantTool { Id = 3, Name = "Integration", Type = "Integration_Amais", IsActive = true, PermissionsEnabled = true, Config = "{\"areSkillsEnabled\":true}" };
            var agent = Agent(new AgentTool { Tool = tool });
            var kernel = BuildKernel();

            var skillsEnabled = await InvokeAsync(kernel, agent, Mock.Of<IMcpClientService>(), ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            Assert.False(skillsEnabled);
            Assert.Empty(kernel.Plugins);
            Assert.Empty(kernel.Data);
        }

        [Fact]
        public async Task PermissionsOff_IgnoresSavedBlock()
        {
            var tool = new TenantTool { Id = 3, Name = "Integration", Type = "Integration_Amais", IsActive = true, PermissionsEnabled = false, Config = "{\"areSkillsEnabled\":false}" };
            var agent = Agent(new AgentTool { Tool = tool, ToolPermission = "Block" });
            var kernel = BuildKernel();

            await InvokeAsync(kernel, agent, Mock.Of<IMcpClientService>(), ToolPermissionPolicy.Build(agent, approvalsAvailable: true));

            Assert.Contains(kernel.Plugins, p => p.Name == "plugin_3_Integration_Amais_Knowledge");
        }
    }
}
