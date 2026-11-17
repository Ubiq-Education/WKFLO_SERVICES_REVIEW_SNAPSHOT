using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.CreateTenantDxpIntegration;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.DxpIntegrations.Commands.CreateTenantDxpIntegration
{
    public class CreateTenantDxpIntegrationCommandHandlerTesting
    {
        public CreateTenantDxpIntegrationCommandHandler CreateTenantDxpIntegrationCommandHandler { get; set; }

        public CreateTenantDxpIntegrationCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = "Test" };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var dxpIntegrations = new List<DxpIntegration>().BuildMockDbSet();
            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.DxpIntegrations).Returns(dxpIntegrations.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            CreateTenantDxpIntegrationCommandHandler = new CreateTenantDxpIntegrationCommandHandler(
                db.Object,
                mapper,
                tenantContext
            );
        }
    }
}
