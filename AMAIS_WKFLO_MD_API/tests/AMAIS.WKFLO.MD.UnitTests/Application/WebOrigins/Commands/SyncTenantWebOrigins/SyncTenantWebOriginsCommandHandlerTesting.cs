using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.WebOrigins.Commands.SyncTenantWebOrigins;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.WebOrigins.Commands.SyncTenantWebOrigins
{
    public class SyncTenantWebOriginsCommandHandlerTesting
    {
        public SyncTenantWebOriginsCommandHandler SyncTenantWebOriginsCommandHandler { get; set; }

        public SyncTenantWebOriginsCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant()
            {
                Id = 1,
                Name = tenantContext.TenantName
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var webOrigins = new List<WebOrigin>() { }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.WebOrigins).Returns(webOrigins.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            SyncTenantWebOriginsCommandHandler = new SyncTenantWebOriginsCommandHandler
            (
                db.Object,
                mapper,
                tenantContext
            );
        }
    }
}
