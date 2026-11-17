using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentToolPermissions;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetTenantAgentToolPermissions
{
    public class GetTenantAgentToolPermissionsQueryHandlerTests : IClassFixture<GetTenantAgentToolPermissionsQueryHandlerTesting>
    {
        private readonly GetTenantAgentToolPermissionsQueryHandlerTesting _testing;

        public GetTenantAgentToolPermissionsQueryHandlerTests(GetTenantAgentToolPermissionsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        private async Task<(Result Result, GetTenantAgentToolPermissionsQueryHandlerTesting.Seed Seed)> RunAsync(int? agentId = null)
        {
            var seed = await _testing.SeedAsync();
            await using var db = _testing.CreateDb(seed);
            var handler = new GetTenantAgentToolPermissionsQueryHandler(db, _testing.TenantContext);
            var result = await handler.Handle(new GetTenantAgentToolPermissionsQuery { Id = agentId ?? seed.AgentId }, TestContext.Current.CancellationToken);
            return (result, seed);
        }

        [Fact]
        public async Task Handle_UnknownAgent_ReturnsNotFound()
        {
            var (result, seed) = await RunAsync(agentId: 9999);
            await using var _ = seed.Connection;

            Assert.Equal(ResultType.NotFound, result.ResultType);
        }

        [Fact]
        public async Task Handle_ReturnsEveryNonDeletedLinkedTool_OrderedByName()
        {
            var (result, seed) = await RunAsync();
            await using var _ = seed.Connection;

            var data = Assert.IsType<SuccessResult<TenantAgentToolPermissionsResult>>(result).Data;
            Assert.Equal(new[] { "Contact form", "Docs MCP", "Inactive HTTP", "Weather HTTP" }, data.Tools.Select(t => t.Name));
        }

        [Fact]
        public async Task Handle_McpTool_ListsEnabledSnapshotFunctionsWithSavedOrDefaultPermission()
        {
            var (result, seed) = await RunAsync();
            await using var _ = seed.Connection;

            var mcp = Assert.IsType<SuccessResult<TenantAgentToolPermissionsResult>>(result).Data.Tools.Single(t => t.ToolId == seed.McpToolId);
            Assert.Null(mcp.Permission);
            Assert.True(mcp.PermissionsEnabled);
            Assert.True(mcp.IsActive);
            Assert.Equal("MCP", mcp.Type);
            Assert.Equal(new[] { "search", "delete" }, mcp.Functions.Select(f => f.Name));
            // Decision 3: an enabled function with nothing saved reads back as Block.
            Assert.Equal("Block", mcp.Functions[0].Permission);
            Assert.Equal("Block", mcp.Functions[1].Permission);
            Assert.Equal("search description", mcp.Functions[0].Description);

            // "Also used by": other non-deleted TENANT agents only (not the published copy, not itself).
            var usedBy = Assert.Single(mcp.UsedByAgents);
            Assert.Equal(seed.OtherAgentId, usedBy.Id);
            Assert.Equal("Sales Agent", usedBy.Name);
        }

        [Fact]
        public async Task Handle_NonMcpTools_HaveOneToolPermissionAndNoFunctions()
        {
            var (result, seed) = await RunAsync();
            await using var _ = seed.Connection;

            var tools = Assert.IsType<SuccessResult<TenantAgentToolPermissionsResult>>(result).Data.Tools;

            var http = tools.Single(t => t.ToolId == seed.HttpToolId);
            Assert.Equal("Ask", http.Permission);
            Assert.Null(http.Functions);
            Assert.Empty(http.UsedByAgents); // the only other link is a deleted agent

            var inactive = tools.Single(t => t.ToolId == seed.InactiveToolId);
            Assert.False(inactive.IsActive);
            Assert.Equal("Block", inactive.Permission); // nothing saved => Block

            // Permissions off: the saved value is kept in the database but not reported.
            var contactForm = tools.Single(t => t.ToolId == seed.ContactFormToolId);
            Assert.False(contactForm.PermissionsEnabled);
            Assert.Null(contactForm.Permission);
        }
    }
}
