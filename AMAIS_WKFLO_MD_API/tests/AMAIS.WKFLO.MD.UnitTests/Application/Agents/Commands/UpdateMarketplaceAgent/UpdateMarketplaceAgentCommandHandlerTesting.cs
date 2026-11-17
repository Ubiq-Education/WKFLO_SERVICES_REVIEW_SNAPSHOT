using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateMarketplaceAgent;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UpdateMarketplaceAgent
{
    public class UpdateMarketplaceAgentCommandHandlerTesting
    {
        public UpdateMarketplaceAgentCommandHandler UpdateMarketplaceAgentCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int MarketplaceAgentId { get; set; } = 1;

        public UpdateMarketplaceAgentCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();
            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var marketplaceAgent = new MarketplaceAgent()
            {
                Id = MarketplaceAgentId,
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                AgentTools = new List<AgentTool>(),
                CreatedOn = fixedDate
            };

            var marketplaceAgents = new List<MarketplaceAgent>() { marketplaceAgent }.BuildMockDbSet();
            var tenants = new List<Tenant>() { }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceAgents).Returns(marketplaceAgents.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UpdateMarketplaceAgentCommandHandler = new UpdateMarketplaceAgentCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
