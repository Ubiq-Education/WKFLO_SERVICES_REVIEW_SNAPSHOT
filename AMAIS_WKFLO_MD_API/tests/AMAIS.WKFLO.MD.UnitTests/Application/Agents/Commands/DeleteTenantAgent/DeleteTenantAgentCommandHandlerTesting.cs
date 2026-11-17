using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgent;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.DeleteTenantAgent
{
    public class DeleteTenantAgentCommandHandlerTesting
    {
        public DeleteTenantAgentCommandHandler DeleteTenantAgentCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int TenantAgentId { get; set; } = 1;
        public int TenantAgentWithPublishedAgentId { get; set; } = 3;
        public int PublishedTenantAgentId { get; set; } = 4;

        // Kept as a direct reference so tests can assert the same object instance the handler mutated
        // (IsDeleted is a plain property write, visible regardless of DbSet mocking).
        public PublishedTenantAgent PublishedTenantAgent { get; set; }

        public DeleteTenantAgentCommandHandlerTesting()
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
                AgentTools = new List<AgentTool>(),
                CreatedOn = fixedDate
            };

            var tenantAgentWithPublishedAgent = new TenantAgent()
            {
                Id = TenantAgentWithPublishedAgentId,
                Name = "Test Name With Published Agent",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                AgentTools = new List<AgentTool>(),
                CreatedOn = fixedDate
            };

            var publishedTenantAgent = new PublishedTenantAgent()
            {
                Id = PublishedTenantAgentId,
                CreatedByTenantAgentId = TenantAgentWithPublishedAgentId,
                Name = "Test Name With Published Agent",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                AgentTools = new List<AgentTool>(),
                CreatedOn = fixedDate
            };

            PublishedTenantAgent = publishedTenantAgent;

            var tenantAgents = new List<TenantAgent>() { tenantAgent, tenantAgentWithPublishedAgent }.BuildMockDbSet();
            var publishedTenantAgents = new List<PublishedTenantAgent>() { publishedTenantAgent }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(publishedTenantAgents.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            DeleteTenantAgentCommandHandler = new DeleteTenantAgentCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
