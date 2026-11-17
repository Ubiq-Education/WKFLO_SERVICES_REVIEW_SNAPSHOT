using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetTenantToolById;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Queries.GetTenantToolById
{
    public class GetTenantToolByIdQueryHandlerTests : IClassFixture<GetTenantToolByIdQueryHandlerTesting>
    {
        private readonly GetTenantToolByIdQueryHandlerTesting _testing;

        public GetTenantToolByIdQueryHandlerTests(GetTenantToolByIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.GetTenantToolByIdQueryHandler.Handle(new GetTenantToolByIdQuery()
            {
                Id = _testing.TenantToolId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<TenantToolGetResult> success)
            {
                Assert.NotNull(success.Data);
                // Regression test: TenantToolGetResult is a hand-authored DTO (Mapster convention mapping
                // doesn't auto-add new entity fields to it) — McpOAuthConnectionId must be explicitly
                // declared on the class for it to survive the mapping, not just present on RunableTool.
                Assert.Equal(_testing.McpOAuthConnectionId, success.Data.McpOAuthConnectionId);
            }
            else
            {
                Assert.Fail("Error on TestHandler_Ok");
            }
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.GetTenantToolByIdQueryHandler.Handle(new GetTenantToolByIdQuery()
            {
                Id = GetTenantToolByIdQueryHandlerTesting.RandomNumber.Next()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
