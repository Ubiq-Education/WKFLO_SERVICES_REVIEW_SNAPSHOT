using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Queries.GetTenantDxpIntegration;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.DxpIntegrations.Queries.GetTenantDxpIntegration
{
    public class GetTenantDxpIntegrationQueryHandlerTesting
    {
        public GetTenantDxpIntegrationQueryHandler GetTenantDxpIntegrationQueryHandler { get; set; }

        public GetTenantDxpIntegrationQueryHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = "Test" };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var integration = new DxpIntegration
            {
                Id = 1,
                TenantId = 1,
                Tenant = tenant,
                DxpTenantName = "dxp-test-tenant",
                Domain = "https://dxp.example.com",
                ApiKey = "api-key-123",
                IsActive = true,
                IsDeleted = false
            };

            var dxpIntegrations = new List<DxpIntegration> { integration }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.DxpIntegrations).Returns(dxpIntegrations.Object);

            GetTenantDxpIntegrationQueryHandler = new GetTenantDxpIntegrationQueryHandler(
                db.Object,
                tenantContext
            );
        }
    }
}
