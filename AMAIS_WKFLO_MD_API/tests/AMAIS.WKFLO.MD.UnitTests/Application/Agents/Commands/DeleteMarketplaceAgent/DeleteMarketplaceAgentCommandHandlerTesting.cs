using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgent;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.DeleteMarketplaceAgent
{
    public class DeleteMarketplaceAgentCommandHandlerTesting
    {
        public DeleteMarketplaceAgentCommandHandler DeleteMarketplaceAgentCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int MarketplaceAgentId { get; set; } = 1;

        public DeleteMarketplaceAgentCommandHandlerTesting()
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
                CreatedOn = fixedDate,
                SourcePublishedAgentId = 1
            };

            var marketplaceAgents = new List<MarketplaceAgent>() { marketplaceAgent }.BuildMockDbSet();

            var publishedTenantAgent = new PublishedTenantAgent()
            {
                Id = 1,
                MarketplaceAgentId = MarketplaceAgentId
            };

            var publishedTenantAgents = new List<PublishedTenantAgent>() { publishedTenantAgent }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceAgents).Returns(marketplaceAgents.Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(publishedTenantAgents.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            DeleteMarketplaceAgentCommandHandler = new DeleteMarketplaceAgentCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
