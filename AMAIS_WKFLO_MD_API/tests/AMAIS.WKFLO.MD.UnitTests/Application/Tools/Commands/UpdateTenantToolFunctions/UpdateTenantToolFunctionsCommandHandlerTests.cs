using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantToolFunctions;
using Moq;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.UpdateTenantToolFunctions
{
    public class UpdateTenantToolFunctionsCommandHandlerTests : IClassFixture<UpdateTenantToolFunctionsCommandHandlerTesting>
    {
        private readonly UpdateTenantToolFunctionsCommandHandlerTesting _testing;

        public UpdateTenantToolFunctionsCommandHandlerTests(UpdateTenantToolFunctionsCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId, new[] { "search", "create" }, ResultType.Ok)]
        [InlineData(UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId, new string[0], ResultType.Ok)]
        [InlineData(UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId, new[] { "removed_one" }, ResultType.Ok)]
        [InlineData(UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId, new[] { "never_listed" }, ResultType.Invalid)]
        [InlineData(UpdateTenantToolFunctionsCommandHandlerTesting.HttpToolId, new[] { "search" }, ResultType.Invalid)]
        [InlineData(999, new[] { "search" }, ResultType.NotFound)]
        public async Task Handle_ReturnsExpectedResultType(int toolId, string[] enabled, ResultType expected)
        {
            var setup = _testing.Create();

            var result = await setup.Handler.Handle(new UpdateTenantToolFunctionsCommand { Id = toolId, EnabledFunctions = enabled.ToList() }, TestContext.Current.CancellationToken);

            Assert.Equal(expected, result.ResultType);
        }

        [Fact]
        public async Task Handle_SavesAllowlistOnTenantToolOnly_AndClearsNewFlags()
        {
            var setup = _testing.Create();

            var result = await setup.Handler.Handle(new UpdateTenantToolFunctionsCommand
            {
                Id = UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId,
                EnabledFunctions = ["create", "create"]
            }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<ToolFunctionsResult>>(result);
            Assert.Equal(new[] { "create" }, ToolPermissionJson.ParseEnabledFunctions(setup.McpTool.EnabledFunctions));
            Assert.All(ToolPermissionJson.ParseDiscoveredFunctions(setup.McpTool.DiscoveredFunctions), f => Assert.False(f.IsNew));
            Assert.All(success.Data.Functions, f => Assert.Equal(ToolFunctionStatus.Active, f.Status));

            // Draft/publish model: the published copy only changes on publish.
            Assert.Null(setup.PublishedCopy.EnabledFunctions);
            Assert.Null(setup.PublishedCopy.DiscoveredFunctions);
            Assert.Null(setup.DeletedCopy.EnabledFunctions);

            setup.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_UnknownFunction_DoesNotSave()
        {
            var setup = _testing.Create();

            var result = await setup.Handler.Handle(new UpdateTenantToolFunctionsCommand
            {
                Id = UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId,
                EnabledFunctions = ["search", "not_offered"]
            }, TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Equal(ErrorConst.ToolFunctionNotAvailable, Assert.Single(invalid.Errors).Code);
            setup.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_WithoutFunctions_KeepsExistingDisplayValues()
        {
            var setup = _testing.Create();
            setup.McpTool.DiscoveredFunctions = ToolPermissionJson.SerializeDiscoveredFunctions(new[]
            {
                new McpFunctionDescriptor { Name = "search", Description = "raw", DisplayName = "Search content", DisplayDescription = "Finds content", IsNew = true }
            });

            var result = await setup.Handler.Handle(new UpdateTenantToolFunctionsCommand
            {
                Id = UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId,
                EnabledFunctions = ["search"]
            }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<ToolFunctionsResult>>(result);
            var stored = Assert.Single(ToolPermissionJson.ParseDiscoveredFunctions(setup.McpTool.DiscoveredFunctions));
            Assert.False(stored.IsNew);
            Assert.Equal("raw", stored.Description);
            Assert.Equal("Search content", stored.DisplayName);
            Assert.Equal("Finds content", stored.DisplayDescription);
            Assert.Equal("Search content", success.Data.Functions[0].DisplayName);
        }

        [Fact]
        public async Task Handle_WithFunctions_SetsTrimmedValues_ClearsBlank_LeavesUnlistedAndServerValuesAlone()
        {
            var setup = _testing.Create();
            setup.McpTool.DiscoveredFunctions = ToolPermissionJson.SerializeDiscoveredFunctions(new[]
            {
                new McpFunctionDescriptor { Name = "search", Description = "raw search", DisplayName = "Old name", DisplayDescription = "Old description" },
                new McpFunctionDescriptor { Name = "create", Description = "raw create", DisplayName = "Keep me" }
            });

            var result = await setup.Handler.Handle(new UpdateTenantToolFunctionsCommand
            {
                Id = UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId,
                EnabledFunctions = ["search"],
                Functions = [new UpdateTenantToolFunctionDisplayInput { Name = "search", DisplayName = "  Search content  ", DisplayDescription = "   " }]
            }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<ToolFunctionsResult>>(result);
            var stored = ToolPermissionJson.ParseDiscoveredFunctions(setup.McpTool.DiscoveredFunctions);
            Assert.Equal(new[] { "search", "create" }, stored.Select(f => f.Name));
            Assert.Equal("raw search", stored[0].Description);
            Assert.Equal("Search content", stored[0].DisplayName);
            Assert.Null(stored[0].DisplayDescription);
            Assert.Equal("raw create", stored[1].Description);
            Assert.Equal("Keep me", stored[1].DisplayName);

            var search = success.Data.Functions.Single(f => f.Name == "search");
            Assert.Equal("Search content", search.DisplayName);
            Assert.Null(search.DisplayDescription);
            setup.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_EntryWithNullValues_ClearsBothFields_NullIsNotLeaveUnchanged()
        {
            var setup = _testing.Create();
            setup.McpTool.DiscoveredFunctions = ToolPermissionJson.SerializeDiscoveredFunctions(new[]
            {
                new McpFunctionDescriptor { Name = "search", DisplayName = "Old name", DisplayDescription = "Old description" },
                new McpFunctionDescriptor { Name = "create", DisplayName = "Untouched", DisplayDescription = "Untouched description" }
            });

            var result = await setup.Handler.Handle(new UpdateTenantToolFunctionsCommand
            {
                Id = UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId,
                EnabledFunctions = ["search"],
                Functions = [new UpdateTenantToolFunctionDisplayInput { Name = "search", DisplayName = null, DisplayDescription = null }]
            }, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<ToolFunctionsResult>>(result);
            var stored = ToolPermissionJson.ParseDiscoveredFunctions(setup.McpTool.DiscoveredFunctions);
            Assert.Null(stored[0].DisplayName);
            Assert.Null(stored[0].DisplayDescription);
            Assert.Equal("Untouched", stored[1].DisplayName);
            Assert.Equal("Untouched description", stored[1].DisplayDescription);
        }

        [Fact]
        public async Task Handle_NullFunctionEntry_IsInvalid_AndDoesNotSave()
        {
            var setup = _testing.Create();
            var before = setup.McpTool.DiscoveredFunctions;
            var enabledBefore = setup.McpTool.EnabledFunctions;

            var result = await setup.Handler.Handle(new UpdateTenantToolFunctionsCommand
            {
                Id = UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId,
                EnabledFunctions = ["search"],
                Functions = [null]
            }, TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Equal(ErrorConst.ToolFunctionNotAvailable, Assert.Single(invalid.Errors).Code);
            Assert.Equal(before, setup.McpTool.DiscoveredFunctions);
            Assert.Equal(enabledBefore, setup.McpTool.EnabledFunctions);
            setup.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("never_listed")]
        [InlineData("removed_one")]
        [InlineData(null)]
        public async Task Handle_DisplayValuesForFunctionOutsideSnapshot_IsInvalid_AndDoesNotSave(string name)
        {
            var setup = _testing.Create();
            var before = setup.McpTool.DiscoveredFunctions;

            var result = await setup.Handler.Handle(new UpdateTenantToolFunctionsCommand
            {
                Id = UpdateTenantToolFunctionsCommandHandlerTesting.McpToolId,
                EnabledFunctions = ["search"],
                Functions = [new UpdateTenantToolFunctionDisplayInput { Name = name, DisplayName = "Friendly" }]
            }, TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Equal(ErrorConst.ToolFunctionNotAvailable, Assert.Single(invalid.Errors).Code);
            Assert.Equal(before, setup.McpTool.DiscoveredFunctions);
            setup.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
