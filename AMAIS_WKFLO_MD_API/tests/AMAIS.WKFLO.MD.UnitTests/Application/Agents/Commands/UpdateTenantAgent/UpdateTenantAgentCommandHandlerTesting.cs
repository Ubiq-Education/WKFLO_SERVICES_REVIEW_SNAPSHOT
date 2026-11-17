using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgent;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UpdateTenantAgent
{
    public class UpdateTenantAgentCommandHandlerTesting
    {
        public UpdateTenantAgentCommandHandler UpdateTenantAgentCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int TenantAgentId { get; set; } = 1;

        public UpdateTenantAgentCommandHandlerTesting()
        {
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
                AgentTools = [],
                IsActive = true,
                CreatedOn = fixedDate
            };

            var tenantAgents = new List<TenantAgent>() { tenantAgent }.BuildMockDbSet();
            var tenants = new List<Tenant>() { }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UpdateTenantAgentCommandHandler = new UpdateTenantAgentCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
