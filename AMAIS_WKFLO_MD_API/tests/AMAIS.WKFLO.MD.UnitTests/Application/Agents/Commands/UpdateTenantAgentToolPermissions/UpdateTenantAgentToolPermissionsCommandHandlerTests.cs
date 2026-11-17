using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgentToolPermissions;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetTenantAgentToolPermissions;
using AMAIS.WKFLO.MD.UnitTests.Application.Tools;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UpdateTenantAgentToolPermissions
{
    public class UpdateTenantAgentToolPermissionsCommandHandlerTests : IClassFixture<UpdateTenantAgentToolPermissionsCommandHandlerTesting>
    {
        private readonly UpdateTenantAgentToolPermissionsCommandHandlerTesting _testing;

        public UpdateTenantAgentToolPermissionsCommandHandlerTests(UpdateTenantAgentToolPermissionsCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        private async Task<Result> RunAsync(GetTenantAgentToolPermissionsQueryHandlerTesting.Seed seed, params UpdateTenantAgentToolPermissionInput[] tools)
        {
            await using var db = _testing.CreateDb(seed);
            var handler = new UpdateTenantAgentToolPermissionsCommandHandler(db, _testing.TenantContext);
            return await handler.Handle(new UpdateTenantAgentToolPermissionsCommand { Id = seed.AgentId, Tools = tools.ToList() }, TestContext.Current.CancellationToken);
        }

        private async Task<(string ToolPermission, string FunctionPermissions)> ReadLinkAsync(GetTenantAgentToolPermissionsQueryHandlerTesting.Seed seed, int toolId)
        {
            await using var db = _testing.CreateDb(seed);
            var link = await db.AgentTools.AsNoTracking().SingleAsync(at => at.AgentId == seed.AgentId && at.ToolId == toolId, TestContext.Current.CancellationToken);
            return (link.ToolPermission, link.FunctionPermissions);
        }

        private static UpdateTenantAgentToolFunctionPermissionInput Fn(string name, string permission) => new() { Name = name, Permission = permission };

        [Fact]
        public async Task Handle_SavesNonMcpAndMcpPermissions_AndReturnsUpdatedView()
        {
            var seed = await _testing.SeedAsync();
            await using var _ = seed.Connection;

            var result = await RunAsync(seed,
                new UpdateTenantAgentToolPermissionInput { ToolId = seed.HttpToolId, Permission = ToolPermissionValues.Block },
                new UpdateTenantAgentToolPermissionInput { ToolId = seed.McpToolId, Functions = [Fn("search", "Ask"), Fn("delete", "Allow")] });

            var data = Assert.IsType<SuccessResult<TenantAgentToolPermissionsResult>>(result).Data;
            Assert.Equal("Block", data.Tools.Single(t => t.ToolId == seed.HttpToolId).Permission);
            var mcp = data.Tools.Single(t => t.ToolId == seed.McpToolId);
            Assert.Equal(new[] { "Ask", "Allow" }, mcp.Functions.Select(f => f.Permission));

            Assert.Equal(("Block", (string)null), await ReadLinkAsync(seed, seed.HttpToolId));
            var (mcpToolPermission, mcpFunctions) = await ReadLinkAsync(seed, seed.McpToolId);
            Assert.Null(mcpToolPermission);
            Assert.Equal(new Dictionary<string, string> { ["search"] = "Ask", ["delete"] = "Allow" }, ToolPermissionJson.ParseFunctionPermissions(mcpFunctions));
        }

        [Fact]
        public async Task Handle_ToolsNotIncluded_KeepTheirSettings_NullPermissionResetsToDefault()
        {
            var seed = await _testing.SeedAsync();
            await using var _ = seed.Connection;

            var result = await RunAsync(seed, new UpdateTenantAgentToolPermissionInput { ToolId = seed.HttpToolId, Permission = null });

            Assert.Equal(ResultType.Ok, result.ResultType);
            Assert.Equal(((string)null, (string)null), await ReadLinkAsync(seed, seed.HttpToolId));
            Assert.Equal("{\"delete\":\"Block\"}", (await ReadLinkAsync(seed, seed.McpToolId)).FunctionPermissions);
        }

        [Fact]
        public async Task Handle_McpToolWithNoFunctions_ClearsAllFunctionPermissions_WhichReadBackAsBlock()
        {
            var seed = await _testing.SeedAsync();
            await using var _ = seed.Connection;

            var result = await RunAsync(seed, new UpdateTenantAgentToolPermissionInput { ToolId = seed.McpToolId });

            Assert.Null((await ReadLinkAsync(seed, seed.McpToolId)).FunctionPermissions);
            var mcp = Assert.IsType<SuccessResult<TenantAgentToolPermissionsResult>>(result).Data.Tools.Single(t => t.ToolId == seed.McpToolId);
            Assert.All(mcp.Functions, f => Assert.Equal("Block", f.Permission));
        }

        [Fact]
        public async Task Handle_McpFunctionOmitted_ReadsBackAsBlock()
        {
            var seed = await _testing.SeedAsync();
            await using var _ = seed.Connection;

            var result = await RunAsync(seed, new UpdateTenantAgentToolPermissionInput { ToolId = seed.McpToolId, Functions = [Fn("search", "Allow")] });

            var mcp = Assert.IsType<SuccessResult<TenantAgentToolPermissionsResult>>(result).Data.Tools.Single(t => t.ToolId == seed.McpToolId);
            Assert.Equal("Allow", mcp.Functions.Single(f => f.Name == "search").Permission);
            Assert.Equal("Block", mcp.Functions.Single(f => f.Name == "delete").Permission);
        }

        public static TheoryData<string, Func<GetTenantAgentToolPermissionsQueryHandlerTesting.Seed, UpdateTenantAgentToolPermissionInput>> InvalidInputs => new()
        {
            { ErrorConst.ToolNotLinkedToAgent, s => new() { ToolId = s.UnlinkedToolId, Permission = "Ask" } },
            { ErrorConst.ToolNotLinkedToAgent, s => new() { ToolId = s.DeletedToolId, Permission = "Ask" } },
            { ErrorConst.ToolPermissionsNotEnabled, s => new() { ToolId = s.ContactFormToolId, Permission = "Ask" } },
            { ErrorConst.ToolPermissionNotForMcp, s => new() { ToolId = s.McpToolId, Permission = "Ask" } },
            { ErrorConst.ToolFunctionsOnlyForMcp, s => new() { ToolId = s.HttpToolId, Functions = [Fn("search", "Ask")] } },
            { ErrorConst.ToolFunctionNotEnabled, s => new() { ToolId = s.McpToolId, Functions = [Fn("create", "Ask")] } },
            { ErrorConst.ToolPermissionInvalid, s => new() { ToolId = s.HttpToolId, Permission = "Sometimes" } },
            { ErrorConst.ToolPermissionInvalid, s => new() { ToolId = s.McpToolId, Functions = [Fn("search", "ask")] } }
        };

        [Theory]
        [MemberData(nameof(InvalidInputs))]
        public async Task Handle_InvalidEntry_Returns400AndChangesNothing(string expectedCode, Func<GetTenantAgentToolPermissionsQueryHandlerTesting.Seed, UpdateTenantAgentToolPermissionInput> invalid)
        {
            var seed = await _testing.SeedAsync();
            await using var _ = seed.Connection;

            // A valid entry listed first must not be applied when a later one is invalid.
            var result = await RunAsync(seed,
                new UpdateTenantAgentToolPermissionInput { ToolId = seed.InactiveToolId, Permission = "Block" },
                invalid(seed));

            var error = Assert.IsType<InvalidResult>(result);
            Assert.Equal(expectedCode, Assert.Single(error.Errors).Code);
            Assert.Null((await ReadLinkAsync(seed, seed.InactiveToolId)).ToolPermission);
        }

        // Bug 7856: with no enabled function (NULL or []) every function is allowed, so a permission may be saved for any
        // function the tool reported; names the tool never reported are still rejected. A non-empty allowlist is unchanged.
        [Theory]
        [InlineData(null, "create", null)]
        [InlineData("[]", "search", null)]
        [InlineData(null, "never_reported", ErrorConst.ToolFunctionNotEnabled)]
        [InlineData("[\"search\"]", "search", null)]
        [InlineData("[\"search\"]", "create", ErrorConst.ToolFunctionNotEnabled)]
        public void Validate_McpFunction_EmptyAllowlistAcceptsEveryDiscoveredFunction(string enabled, string function, string expectedError)
        {
            var tool = ToolPermissionsTestData.McpTool(7, snapshot: ToolPermissionsTestData.Snapshot(("search", false), ("create", true)), enabled: enabled);
            var linked = new Dictionary<int, AgentTool> { [tool.Id] = new AgentTool { Tool = tool, ToolId = tool.Id } };

            var error = UpdateTenantAgentToolPermissionsCommandHandler.Validate(
                new UpdateTenantAgentToolPermissionInput { ToolId = tool.Id, Functions = [Fn(function, "Allow")] }, linked);

            Assert.Equal(expectedError, error);
        }
    }
}
