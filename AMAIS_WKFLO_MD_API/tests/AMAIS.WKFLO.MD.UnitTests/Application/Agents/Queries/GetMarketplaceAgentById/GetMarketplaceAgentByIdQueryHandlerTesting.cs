using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentById;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetMarketplaceAgentById
{
    public class GetMarketplaceAgentByIdQueryHandlerTesting
    {
        public GetMarketplaceAgentByIdQueryHandler GetMarketplaceAgentByIdQueryHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int MarketplaceAgentId { get; private set; } = RandomNumber.Next();
        public int UnratedMarketplaceAgentId { get; private set; } = RandomNumber.Next();

        public GetMarketplaceAgentByIdQueryHandlerTesting()
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

            var marketplaceAgent2 = new MarketplaceAgent()
            {
                Id = UnratedMarketplaceAgentId,
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2",
                AgentTools = new List<AgentTool>(),
                CreatedOn = fixedDate
            };

            var marketplaceAgents = new List<MarketplaceAgent>() {
                marketplaceAgent, marketplaceAgent2
            }.BuildMockDbSet();

            // Two live installs of `marketplaceAgent`, none for marketplaceAgent2 - exercises the
            // live-computed Installations count (see GetMarketplaceAgentByIdQueryHandler).
            var tenantAgents = new List<TenantAgent>()
            {
                new TenantAgent
                {
                    Id = RandomNumber.Next(),
                    Name = "Installed Copy 1",
                    Description = "d",
                    SystemPrompt = "p",
                    TenantId = 1,
                    InstalledFromMarketplaceAgentId = MarketplaceAgentId,
                    CreatedOn = fixedDate
                },
                new TenantAgent
                {
                    Id = RandomNumber.Next(),
                    Name = "Installed Copy 2",
                    Description = "d",
                    SystemPrompt = "p",
                    TenantId = 2,
                    InstalledFromMarketplaceAgentId = MarketplaceAgentId,
                    CreatedOn = fixedDate
                }
            }.BuildMockDbSet();

            // A single rating of 3 for `marketplaceAgent`, none for marketplaceAgent2 - exercises the
            // live-computed Stars average (see GetMarketplaceAgentByIdQueryHandler): one rating => that
            // rating; no ratings => 0.
            var userStars = new List<UserStar>()
            {
                new UserStar { Email = "user1@example.com", Type = nameof(MarketplaceAgent), TypeId = MarketplaceAgentId, Value = 3, IsDeleted = false },
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceAgents).Returns(marketplaceAgents.Object);
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);
            db.Setup(x => x.UserStars).Returns(userStars.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            GetMarketplaceAgentByIdQueryHandler = new GetMarketplaceAgentByIdQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}


