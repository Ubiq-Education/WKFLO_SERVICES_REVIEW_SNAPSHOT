using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.CreateTenantDxpIntegration;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.DxpIntegrations.Commands.CreateTenantDxpIntegration
{
    public class CreateTenantDxpIntegrationCommandHandlerTests : IClassFixture<CreateTenantDxpIntegrationCommandHandlerTesting>
    {
        private readonly CreateTenantDxpIntegrationCommandHandlerTesting _testing;

        public CreateTenantDxpIntegrationCommandHandlerTests(CreateTenantDxpIntegrationCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("dxp-tenant-1", "https://dxp.example.com", "api-key-123", true)]
        [InlineData("dxp-tenant-2", "https://dxp2.example.com", "api-key-456", false)]
        public async Task Handle_ValidCommand_ReturnsSuccessResult(
            string dxpTenantName, string domain, string apiKey, bool isActive)
        {
            var command = new CreateTenantDxpIntegrationCommand
            {
                DxpTenantName = dxpTenantName,
                Domain = domain,
                ApiKey = apiKey,
                IsActive = isActive
            };

            // Act
            var result = await _testing.CreateTenantDxpIntegrationCommandHandler.Handle(
                command, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<DxpIntegrationCreateResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(dxpTenantName, success.Data.DxpTenantName);
                Assert.Equal(domain, success.Data.Domain);
                Assert.Equal(apiKey, success.Data.ApiKey);
                Assert.Equal(isActive, success.Data.IsActive);
            }
        }
    }
}
