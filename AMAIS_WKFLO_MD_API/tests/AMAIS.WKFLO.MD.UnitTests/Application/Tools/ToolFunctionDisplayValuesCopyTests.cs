using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools
{
    /// <summary>
    /// WI 7726: friendly display values live inside <see cref="Tool.DiscoveredFunctions"/>, so the real Mapster
    /// entity copies used by publish (TenantTool -> PublishedTenantTool), share (PublishedTenantTool ->
    /// MarketplaceTool) and install (MarketplaceTool -> TenantTool) must carry them along untouched.
    /// </summary>
    public class ToolFunctionDisplayValuesCopyTests
    {
        [Fact]
        public void PublishShareInstallCopies_KeepDisplayValues()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();
            var snapshot = ToolPermissionJson.SerializeDiscoveredFunctions(new[]
            {
                new McpFunctionDescriptor { Name = "search", Description = "raw", DisplayName = "Search content", DisplayDescription = "Finds content" }
            });
            var tenantTool = ToolPermissionsTestData.McpTool(1, snapshot: snapshot, enabled: ToolPermissionsTestData.Enabled("search"));

            var published = mapper.Map<TenantTool, PublishedTenantTool>(tenantTool);
            var shared = mapper.Map<PublishedTenantTool, MarketplaceTool>(published);
            var installed = mapper.Map<MarketplaceTool, TenantTool>(shared);

            foreach (var copy in new Tool[] { published, shared, installed })
            {
                var function = Assert.Single(ToolPermissionJson.ParseDiscoveredFunctions(copy.DiscoveredFunctions));
                Assert.Equal("search", function.Name);
                Assert.Equal("raw", function.Description);
                Assert.Equal("Search content", function.DisplayName);
                Assert.Equal("Finds content", function.DisplayDescription);
            }
        }
    }
}
