using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Queries.GetTenantDxpIntegration;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.DxpIntegrations.Queries.GetTenantDxpIntegration
{
    public class GetTenantDxpIntegrationQueryHandlerTests : IClassFixture<GetTenantDxpIntegrationQueryHandlerTesting>
    {
        private readonly GetTenantDxpIntegrationQueryHandlerTesting _testing;

        public GetTenantDxpIntegrationQueryHandlerTests(GetTenantDxpIntegrationQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_IntegrationExists_ReturnsSuccessResult()
        {
            // Act
            var result = await _testing.GetTenantDxpIntegrationQueryHandler.Handle(
                new GetTenantDxpIntegrationQuery(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            var success = Assert.IsType<SuccessResult<DxpIntegrationGetResult>>(result);
            Assert.NotNull(success.Data);
            Assert.Equal("dxp-test-tenant", success.Data.DxpTenantName);
            Assert.Equal("https://dxp.example.com", success.Data.Domain);
            Assert.True(success.Data.IsActive);
        }
    }
}
