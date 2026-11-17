using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.UpdateTenantDxpIntegration;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.DxpIntegrations.Commands.UpdateTenantDxpIntegration
{
    public class UpdateTenantDxpIntegrationCommandHandlerTesting
    {
        public UpdateTenantDxpIntegrationCommandHandler UpdateTenantDxpIntegrationCommandHandler { get; set; }

        public UpdateTenantDxpIntegrationCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = "Test" };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var integration = new DxpIntegration
            {
                Id = 1,
                TenantId = 1,
                Tenant = tenant,
                DxpTenantName = "original-dxp-tenant",
                Domain = "https://original.example.com",
                ApiKey = "original-key",
                IsActive = true,
                IsDeleted = false
            };

            var dxpIntegrations = new List<DxpIntegration> { integration }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.DxpIntegrations).Returns(dxpIntegrations.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UpdateTenantDxpIntegrationCommandHandler = new UpdateTenantDxpIntegrationCommandHandler(
                db.Object,
                tenantContext
            );
        }
    }
}
