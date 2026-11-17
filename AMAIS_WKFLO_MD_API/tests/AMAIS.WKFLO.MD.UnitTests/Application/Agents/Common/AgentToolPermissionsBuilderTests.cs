using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.MD.Application.Agents.Common;
using AMAIS.WKFLO.MD.UnitTests.Application.Tools;
using System.Linq;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Common
{
    /// <summary>WI 7726: the agent tool permissions view exposes each MCP function's friendly display values.</summary>
    public class AgentToolPermissionsBuilderTests
    {
        [Fact]
        public void BuildTool_McpFunctions_ExposeDisplayValues_KeepingRawNameAndDescription()
        {
            var tool = ToolPermissionsTestData.McpTool(1,
                snapshot: ToolPermissionJson.SerializeDiscoveredFunctions(new[]
                {
                    new McpFunctionDescriptor { Name = "search", Description = "raw search", DisplayName = "Search content", DisplayDescription = "Finds content" },
                    new McpFunctionDescriptor { Name = "create", Description = "raw create" },
                    new McpFunctionDescriptor { Name = "hidden", Description = "not enabled", DisplayName = "Hidden" }
                }),
                enabled: ToolPermissionsTestData.Enabled("search", "create"));

            var result = AgentToolPermissionsBuilder.BuildTool(new AgentTool { Tool = tool, ToolId = tool.Id, FunctionPermissions = "{\"search\":\"Allow\"}" }, []);

            Assert.Equal(new[] { "search", "create" }, result.Functions.Select(f => f.Name));

            var search = result.Functions[0];
            Assert.Equal("raw search", search.Description);
            Assert.Equal("Search content", search.DisplayName);
            Assert.Equal("Finds content", search.DisplayDescription);
            Assert.Equal(ToolPermissionValues.Allow, search.Permission);

            var create = result.Functions[1];
            Assert.Equal("raw create", create.Description);
            Assert.Null(create.DisplayName);
            Assert.Null(create.DisplayDescription);
        }

        // Bug 7856: no enabled function (NULL or []) = every function is allowed, so every discovered function is listed;
        // with "Manage permissions" off the allowlist is ignored, so a non-empty one lists every function too.
        [Theory]
        [InlineData(null, true)]
        [InlineData("[]", true)]
        [InlineData(null, false)]
        [InlineData("[\"search\"]", false)]
        public void BuildTool_McpWithNoEnabledFunctions_ListsEveryDiscoveredFunction(string enabled, bool permissionsEnabled)
        {
            var tool = ToolPermissionsTestData.McpTool(1,
                snapshot: ToolPermissionsTestData.Snapshot(("search", false), ("create", true)),
                enabled: enabled,
                permissionsEnabled: permissionsEnabled);

            var result = AgentToolPermissionsBuilder.BuildTool(new AgentTool { Tool = tool, ToolId = tool.Id, FunctionPermissions = "{\"search\":\"Allow\"}" }, []);

            Assert.Equal(new[] { "search", "create" }, result.Functions.Select(f => f.Name));
            Assert.Equal(permissionsEnabled ? new[] { "Allow", "Block" } : new string[] { null, null }, result.Functions.Select(f => f.Permission));
        }
    }
}
