using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.UpdateTenantDxpIntegration;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.DxpIntegrations.Commands.UpdateTenantDxpIntegration
{
    public class UpdateTenantDxpIntegrationCommandHandlerTests : IClassFixture<UpdateTenantDxpIntegrationCommandHandlerTesting>
    {
        private readonly UpdateTenantDxpIntegrationCommandHandlerTesting _testing;

        public UpdateTenantDxpIntegrationCommandHandlerTests(UpdateTenantDxpIntegrationCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("new-dxp-tenant", "https://new.example.com", "new-key-123", true)]
        [InlineData("updated-dxp-tenant", "https://updated.example.com", "updated-key-456", false)]
        public async Task Handle_ValidCommand_ReturnsSuccessResult(
            string dxpTenantName, string domain, string apiKey, bool isActive)
        {
            var command = new UpdateTenantDxpIntegrationCommand
            {
                Id = 1,
                DxpTenantName = dxpTenantName,
                Domain = domain,
                ApiKey = apiKey,
                IsActive = isActive
            };

            // Act
            var result = await _testing.UpdateTenantDxpIntegrationCommandHandler.Handle(
                command, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<DxpIntegrationUpdateResult> success)
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
