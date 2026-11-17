using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.CreateMarketplaceTool
{
    public class CreateMarketplaceToolCommandHandlerTesting
    {
        public CreateMarketplaceToolCommandHandler CreateMarketplaceToolCommandHandler { get; set; }

        public CreateMarketplaceToolCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant()
            {
                Id = 1,
                Name = tenantContext.TenantName
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var marketplaceTools = new List<MarketplaceTool>() { }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceTools).Returns(marketplaceTools.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            CreateMarketplaceToolCommandHandler = new CreateMarketplaceToolCommandHandler
            (
                db.Object,
                mapper,
                tenantContext,
                Mock.Of<IMcpTokenProtector>()
            );
        }
    }
}
