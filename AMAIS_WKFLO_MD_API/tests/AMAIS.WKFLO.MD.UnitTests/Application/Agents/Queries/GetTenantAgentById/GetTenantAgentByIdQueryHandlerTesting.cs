using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentById;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetTenantAgentById
{
    public class GetTenantAgentByIdQueryHandlerTesting
    {
        public GetTenantAgentByIdQueryHandler GetTenantAgentByIdQueryHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int TenantAgentId { get; private set; } = RandomNumber.Next();

        public GetTenantAgentByIdQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant()
            {
                Id = 1,
                Name = tenantContext.TenantName
            };

            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tenantAgent = new TenantAgent()
            {
                Id = TenantAgentId,
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                AgentTools = new List<AgentTool>(),
                IsActive = true,
                CreatedOn = fixedDate,
                Tenant = tenant
            };

            var tenantAgent2 = new TenantAgent()
            {
                Id = RandomNumber.Next(),
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2",
                AgentTools = new List<AgentTool>(),
                IsActive = true,
                CreatedOn = fixedDate,
                Tenant = tenant
            };

            var tenantAgents = new List<TenantAgent>() {
                tenantAgent, tenantAgent2
            }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();

            var entityTags = new List<EntityTag>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.EntityTags).Returns(entityTags.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            GetTenantAgentByIdQueryHandler = new GetTenantAgentByIdQueryHandler
            (
                db.Object,
                mapper,
                tenantContext
            );
        }
    }
}


