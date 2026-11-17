using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentById;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetTenantAgentById
{
    public class GetTenantAgentByIdQueryHandlerTests : IClassFixture<GetTenantAgentByIdQueryHandlerTesting>
    {
        private readonly GetTenantAgentByIdQueryHandlerTesting _testing;

        public GetTenantAgentByIdQueryHandlerTests(GetTenantAgentByIdQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandler_Ok()
        {
            // Act
            var result = await _testing.GetTenantAgentByIdQueryHandler.Handle(new GetTenantAgentByIdQuery()
            {
                Id = _testing.TenantAgentId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<TenantAgentGetResult> success)
            {
                Assert.NotNull(success.Data);
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
            var result = await _testing.GetTenantAgentByIdQueryHandler.Handle(new GetTenantAgentByIdQuery()
            {
                Id = GetTenantAgentByIdQueryHandlerTesting.RandomNumber.Next()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }
    }
}
