using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tenants.Commands.CreateTenant;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tenants.Commands.CreateTenant
{
    public class CreateTenantCommandHandlerTesting
    {
        public CreateTenantCommandHandler CreateTenantCommandHandler { get; set; }

        public const string ExistingTenantName = "ExistingTenant";

        public CreateTenantCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var existingTenant = new Tenant { Id = 1, Name = ExistingTenantName, Region = "EU" };

            var existingWebOrigin = new WebOrigin
            {
                Id = 1,
                TenantId = 1,
                WebOriginType = "SignalR",
                Name = "https://existing.example.com",
                IsActive = true
            };

            var tenants = new List<Tenant> { existingTenant }.BuildMockDbSet();
            var webOrigins = new List<WebOrigin> { existingWebOrigin }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.WebOrigins).Returns(webOrigins.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            CreateTenantCommandHandler = new CreateTenantCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
