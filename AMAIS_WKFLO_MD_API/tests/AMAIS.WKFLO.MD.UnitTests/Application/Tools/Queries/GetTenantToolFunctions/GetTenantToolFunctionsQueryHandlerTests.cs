using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetTenantToolFunctions;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetTenantToolFunctions
{
    public class GetTenantToolFunctionsQueryHandlerTests : IClassFixture<GetTenantToolFunctionsQueryHandlerTesting>
    {
        private readonly GetTenantToolFunctionsQueryHandlerTesting _testing;

        public GetTenantToolFunctionsQueryHandlerTests(GetTenantToolFunctionsQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(GetTenantToolFunctionsQueryHandlerTesting.McpToolId, ResultType.Ok)]
        [InlineData(GetTenantToolFunctionsQueryHandlerTesting.HttpToolId, ResultType.Invalid)]
        [InlineData(999, ResultType.NotFound)]
        public async Task Handle_ReturnsExpectedResultType(int toolId, ResultType expected)
        {
            var result = await _testing.Handler.Handle(new GetTenantToolFunctionsQuery { Id = toolId }, TestContext.Current.CancellationToken);

            Assert.Equal(expected, result.ResultType);
        }

        [Fact]
        public async Task Handle_McpTool_ReturnsSnapshotWithEnabledFlagsAndStatus()
        {
            var result = await _testing.Handler.Handle(new GetTenantToolFunctionsQuery { Id = GetTenantToolFunctionsQueryHandlerTesting.McpToolId }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<ToolFunctionsResult>>(result);
            Assert.Equal(new[] { "search", "create" }, success.Data.Functions.Select(f => f.Name));
            Assert.True(success.Data.Functions[0].IsEnabled);
            Assert.Equal(ToolFunctionStatus.Active, success.Data.Functions[0].Status);
            Assert.False(success.Data.Functions[1].IsEnabled);
            Assert.Equal(ToolFunctionStatus.New, success.Data.Functions[1].Status);
            Assert.NotNull(success.Data.FunctionsDiscoveredOn);
        }

        [Fact]
        public async Task Handle_NonMcpTool_ReturnsToolMustBeMcp()
        {
            var result = await _testing.Handler.Handle(new GetTenantToolFunctionsQuery { Id = GetTenantToolFunctionsQueryHandlerTesting.HttpToolId }, TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Equal(ErrorConst.ToolMustBeMcp, Assert.Single(invalid.Errors).Code);
        }
    }
}
