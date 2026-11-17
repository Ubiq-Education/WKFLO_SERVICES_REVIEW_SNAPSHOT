using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.CreateTenantAgent
{
    public class CreateTenantAgentCommandHandlerTesting
    {
        public CreateTenantAgentCommandHandler CreateTenantAgentCommandHandler { get; set; }
        public Mock<Microsoft.EntityFrameworkCore.DbSet<TenantAgent>> TenantAgentsDbSetMock { get; set; }

        public CreateTenantAgentCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant()
            {
                Id = 1,
                Name = tenantContext.TenantName
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            var tenantAgent = new List<TenantAgent>() { }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            TenantAgentsDbSetMock = tenantAgent;
            db.Setup(x => x.TenantAgents).Returns(tenantAgent.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            CreateTenantAgentCommandHandler = new CreateTenantAgentCommandHandler
            (
                db.Object,
                mapper,
                tenantContext
            );
        }
    }
}
