using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using System.Collections.Generic;
using System.Linq;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools
{
    /// <summary>WI 7447/7575: pure domain helpers behind the MCP allowlist and permission defaults.</summary>
    public class ToolFunctionsBuilderTests
    {
        [Fact]
        public void Build_NoSnapshotAndNothingEnabled_ReturnsEmptyList()
        {
            var result = ToolFunctionsBuilder.Build(ToolPermissionsTestData.McpTool(1));

            Assert.Empty(result.Functions);
            Assert.Null(result.FunctionsDiscoveredOn);
        }

        [Fact]
        public void Build_ComputesEnabledFlagAndActiveNewRemovedStatus()
        {
            var tool = ToolPermissionsTestData.McpTool(1,
                snapshot: ToolPermissionsTestData.Snapshot(("search", false), ("create", true)),
                enabled: ToolPermissionsTestData.Enabled("search", "gone"));

            var result = ToolFunctionsBuilder.Build(tool);

            Assert.Equal(new[] { "search", "create", "gone" }, result.Functions.Select(f => f.Name));
            var search = result.Functions[0];
            Assert.True(search.IsEnabled);
            Assert.Equal(ToolFunctionStatus.Active, search.Status);
            Assert.Equal("search description", search.Description);
            Assert.NotNull(search.InputSchema);

            var create = result.Functions[1];
            Assert.False(create.IsEnabled);
            Assert.Equal(ToolFunctionStatus.New, create.Status);

            var gone = result.Functions[2];
            Assert.True(gone.IsEnabled);
            Assert.Equal(ToolFunctionStatus.Removed, gone.Status);
            Assert.Null(gone.Description);
            Assert.Equal(tool.FunctionsDiscoveredOn, result.FunctionsDiscoveredOn);
        }

        [Fact]
        public void MergeSnapshot_KeepsKnownFlags_MarksUnseenAsNew_UnlessAlreadyEnabled()
        {
            var previous = new List<McpFunctionDescriptor>
            {
                new() { Name = "a", IsNew = false },
                new() { Name = "b", IsNew = true }
            };
            var fresh = new List<McpFunctionDescriptor>
            {
                new() { Name = "a", Description = "A v2" },
                new() { Name = "b" },
                new() { Name = "c" },
                new() { Name = "d" },
                new() { Name = "c" },
                new() { Name = " " }
            };

            var merged = ToolFunctionsBuilder.MergeSnapshot(previous, fresh, new HashSet<string> { "d" });

            Assert.Equal(new[] { "a", "b", "c", "d" }, merged.Select(f => f.Name));
            Assert.False(merged[0].IsNew);
            Assert.Equal("A v2", merged[0].Description);
            Assert.True(merged[1].IsNew);
            Assert.True(merged[2].IsNew);
            Assert.False(merged[3].IsNew);
        }

        [Fact]
        public void MarkAllReviewed_ClearsNewFlag()
        {
            var reviewed = ToolFunctionsBuilder.MarkAllReviewed(new[] { new McpFunctionDescriptor { Name = "a", IsNew = true } });

            Assert.False(Assert.Single(reviewed).IsNew);
        }

        [Fact]
        public void Build_ExposesDisplayValues_AndNullForRemovedFunctions()
        {
            var tool = ToolPermissionsTestData.McpTool(1,
                snapshot: ToolPermissionJson.SerializeDiscoveredFunctions(new[]
                {
                    new McpFunctionDescriptor { Name = "search", Description = "raw", DisplayName = "Search content", DisplayDescription = "Finds content" },
                    new McpFunctionDescriptor { Name = "create", Description = "raw create" }
                }),
                enabled: ToolPermissionsTestData.Enabled("search", "gone"));

            var result = ToolFunctionsBuilder.Build(tool);

            Assert.Equal("search", result.Functions[0].Name);
            Assert.Equal("raw", result.Functions[0].Description);
            Assert.Equal("Search content", result.Functions[0].DisplayName);
            Assert.Equal("Finds content", result.Functions[0].DisplayDescription);
            Assert.Null(result.Functions[1].DisplayName);
            Assert.Null(result.Functions[1].DisplayDescription);
            Assert.Equal(ToolFunctionStatus.Removed, result.Functions[2].Status);
            Assert.Null(result.Functions[2].DisplayName);
            Assert.Null(result.Functions[2].DisplayDescription);
        }

        [Fact]
        public void MergeSnapshot_CarriesDisplayValuesByName_TakesServerValuesFromFreshResult()
        {
            var previous = new List<McpFunctionDescriptor>
            {
                new() { Name = "kept", Description = "old", DisplayName = "Friendly kept", DisplayDescription = "Friendly kept description" },
                new() { Name = "dropped", DisplayName = "Friendly dropped" }
            };
            var fresh = new List<McpFunctionDescriptor>
            {
                new() { Name = "kept", Description = "new", DisplayName = "ignored from server", DisplayDescription = "ignored from server" },
                new() { Name = "added" }
            };

            var merged = ToolFunctionsBuilder.MergeSnapshot(previous, fresh, null);

            Assert.Equal(new[] { "kept", "added" }, merged.Select(f => f.Name));
            Assert.Equal("new", merged[0].Description);
            Assert.Equal("Friendly kept", merged[0].DisplayName);
            Assert.Equal("Friendly kept description", merged[0].DisplayDescription);
            Assert.Null(merged[1].DisplayName);
            Assert.Null(merged[1].DisplayDescription);
        }

        [Fact]
        public void MarkAllReviewed_KeepsDisplayValues()
        {
            var reviewed = Assert.Single(ToolFunctionsBuilder.MarkAllReviewed(new[]
            {
                new McpFunctionDescriptor { Name = "a", IsNew = true, DisplayName = "Friendly", DisplayDescription = "Friendly description" }
            }));

            Assert.False(reviewed.IsNew);
            Assert.Equal("Friendly", reviewed.DisplayName);
            Assert.Equal("Friendly description", reviewed.DisplayDescription);
        }

        [Fact]
        public void DiscoveredFunctionsJson_OmitsUnsetDisplayValues_AndReadsLegacyRowsAsNull()
        {
            var json = ToolPermissionJson.SerializeDiscoveredFunctions(new[]
            {
                new McpFunctionDescriptor { Name = "plain" },
                new McpFunctionDescriptor { Name = "friendly", DisplayName = "Friendly", DisplayDescription = "Friendly description" }
            });

            Assert.DoesNotContain("\"displayName\":null", json);
            Assert.Contains("\"displayName\":\"Friendly\"", json);
            Assert.Contains("\"displayDescription\":\"Friendly description\"", json);

            var legacy = Assert.Single(ToolPermissionJson.ParseDiscoveredFunctions("[{\"name\":\"search\",\"isNew\":false}]"));
            Assert.Null(legacy.DisplayName);
            Assert.Null(legacy.DisplayDescription);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("   ", null)]
        [InlineData("  Friendly  ", "Friendly")]
        public void NormalizeDisplayValue_TrimsAndTurnsBlankIntoNull(string value, string expected)
        {
            Assert.Equal(expected, McpFunctionDescriptor.NormalizeDisplayValue(value));
        }

        [Fact]
        public void GetEnableableNames_IsSnapshotPlusAlreadyEnabled()
        {
            var tool = ToolPermissionsTestData.McpTool(1,
                snapshot: ToolPermissionsTestData.Snapshot(("a", false)),
                enabled: ToolPermissionsTestData.Enabled("removed"));

            var names = ToolFunctionsBuilder.GetEnableableNames(tool);

            Assert.Equal(new[] { "a", "removed" }, names.OrderBy(n => n));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"unexpected\":true}")]
        public void ParseHelpers_MalformedValues_ReadAsNothingSaved(string json)
        {
            Assert.Empty(ToolPermissionJson.ParseDiscoveredFunctions(json));
            Assert.Empty(ToolPermissionJson.ParseEnabledFunctions(json));
        }

        [Fact]
        public void ParseFunctionPermissions_DropsInvalidValues()
        {
            var parsed = ToolPermissionJson.ParseFunctionPermissions("{\"a\":\"Ask\",\"b\":\"Maybe\",\"c\":\"Block\"}");

            Assert.Equal(2, parsed.Count);
            Assert.Equal(ToolPermissionValues.Ask, parsed["a"]);
            Assert.Equal(ToolPermissionValues.Block, parsed["c"]);
        }

        [Fact]
        public void SerializeFunctionPermissions_EmptyMap_IsNull()
        {
            Assert.Null(ToolPermissionJson.SerializeFunctionPermissions(new Dictionary<string, string>()));
        }

        [Theory]
        [InlineData("Allow", "Allow")]
        [InlineData("Ask", "Ask")]
        [InlineData("Block", "Block")]
        [InlineData(null, "Block")]
        [InlineData("", "Block")]
        [InlineData("allow", "Block")]
        public void NormalizeOrBlock_DefaultsToBlock(string stored, string expected)
        {
            Assert.Equal("Block", ToolPermissionValues.Default);
            Assert.Equal(expected, ToolPermissionValues.NormalizeOrBlock(stored));
        }
    }
}
