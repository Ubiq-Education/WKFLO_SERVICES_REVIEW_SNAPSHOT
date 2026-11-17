using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.RateMarketplaceAgent;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.RateMarketplaceAgent
{
    public class RateMarketplaceAgentCommandHandlerTesting
    {
        public RateMarketplaceAgentCommandHandler RateMarketplaceAgentCommandHandler { get; private set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public Mock<IMasterDataDbContext> MockDb { get; private set; }
        public UserContext UserContext { get; private set; }
        public int MarketplaceAgentId { get; set; } = 1;
        public string UserEmail { get; set; } = "test@example.com";

        public RateMarketplaceAgentCommandHandlerTesting()
        {
            Initialize();
        }

        private void Initialize()
        {
            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            MockDb = new Mock<IMasterDataDbContext>();

            UserContext = new UserContext
            {
                Email = UserEmail,
                UserId = "test-user-id",
                UserName = "Test User",
                Guid = Guid.NewGuid().ToString()
            };

            SetupInitialData();

            RateMarketplaceAgentCommandHandler = new RateMarketplaceAgentCommandHandler(
                MockDb.Object,
                UserContext
            );
        }

        private void SetupInitialData()
        {
            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tenant = new Tenant()
            {
                Id = 1,
                Name = "Test"
            };

            var marketplaceAgent = new MarketplaceAgent()
            {
                Id = MarketplaceAgentId,
                AgentType = nameof(MarketplaceAgent),
                Name = "Test Agent",
                Description = "Test Agent Description",
                SystemPrompt = "You are a helpful assistant",
                AIModelId = null,
                TenantId = tenant.Id,
                Tenant = tenant,
                SourcePublishedAgentId = null,
                CreatedOn = fixedDate,
                LastUpdatedOn = null,
                IsDeleted = false
            };

            var marketplaceAgents = new List<MarketplaceAgent>() { marketplaceAgent }.BuildMockDbSet();
            var userStars = new List<UserStar>().BuildMockDbSet();

            MockDb.Setup(x => x.MarketplaceAgents).Returns(marketplaceAgents.Object);
            MockDb.Setup(x => x.UserStars).Returns(userStars.Object);
            MockDb.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public void SetupNonExistentAgent()
        {
            var emptyMarketplaceAgents = new List<MarketplaceAgent>().BuildMockDbSet();
            MockDb.Setup(x => x.MarketplaceAgents).Returns(emptyMarketplaceAgents.Object);
        }

        public void SetupExistingUserStar(int toolId, int currentRating)
        {
            var existingUserStar = new UserStar
            {
                Id = 1,
                Email = UserEmail,
                Type = nameof(MarketplaceAgent),
                TypeId = toolId,
                Value = currentRating,
                IsDeleted = false,
                CreatedOn = DateTimeProviderMock.Object.DateTimeOffsetUtcNow()
            };

            var userStarsWithExisting = new List<UserStar> { existingUserStar }.BuildMockDbSet();
            MockDb.Setup(x => x.UserStars).Returns(userStarsWithExisting.Object);
        }

        public void VerifyDatabaseSaved()
        {
            MockDb.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        public void VerifyUserStarAdded()
        {
            MockDb.Verify(x => x.UserStars.AddAsync(It.IsAny<UserStar>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        public void Reset()
        {
            Initialize();
        }
    }
}
