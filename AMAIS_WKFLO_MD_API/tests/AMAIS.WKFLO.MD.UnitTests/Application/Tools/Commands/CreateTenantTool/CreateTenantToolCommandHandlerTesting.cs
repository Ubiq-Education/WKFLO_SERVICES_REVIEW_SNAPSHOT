using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.CreateTenantTool
{
    public class CreateTenantToolCommandHandlerTesting
    {
        public CreateTenantToolCommandHandler CreateTenantToolCommandHandler { get; set; }

        public CreateTenantToolCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant()
            {
                Id = 1,
                Name = tenantContext.TenantName
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var aimodels = new List<TenantTool>() { }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(aimodels.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            var settings = Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://wkflo.example.com" });

            CreateTenantToolCommandHandler = new CreateTenantToolCommandHandler
            (
                db.Object,
                mapper,
                tenantContext,
                settings,
                Mock.Of<IMcpTokenProtector>()
            );
        }
    }
}
