using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Commands.RateMarketplaceTool;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.RateMarketplaceTool
{
    public class RateMarketplaceToolCommandHandlerTesting
    {
        public RateMarketplaceToolCommandHandler RateMarketplaceToolCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public Mock<IMasterDataDbContext> MockDb { get; set; } = new Mock<IMasterDataDbContext>();
        public UserContext UserContext { get; set; }
        public int MarketplaceToolId { get; set; } = 1;
        public string UserEmail { get; set; }

        public RateMarketplaceToolCommandHandlerTesting()
        {
            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            UserContext = new UserContext
            {
                Email = UserEmail,
                UserId = "test-user-id",
                UserName = "Test User",
                Guid = Guid.NewGuid().ToString()
            };

            SetupInitialData();

            RateMarketplaceToolCommandHandler = new RateMarketplaceToolCommandHandler(
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

            var marketplaceTool = new MarketplaceTool()
            {
                Id = MarketplaceToolId,
                Name = "Test Tool",
                Description = "Test Tool Description",
                Type = "API",
                Config = "{\"endpoint\": \"https://api.example.com\"}",
                CreatedOn = fixedDate,
                IsDeleted = false
            };

            var marketplaceTools = new List<MarketplaceTool>() { marketplaceTool }.BuildMockDbSet();
            var userStars = new List<UserStar>().BuildMockDbSet();

            MockDb.Setup(x => x.MarketplaceTools).Returns(marketplaceTools.Object);
            MockDb.Setup(x => x.UserStars).Returns(userStars.Object);
            MockDb.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(1));
        }

        public void SetupNonExistentTool()
        {
            var emptyMarketplaceTools = new List<MarketplaceTool>().BuildMockDbSet();
            MockDb.Setup(x => x.MarketplaceTools).Returns(emptyMarketplaceTools.Object);
        }

        public void SetupExistingUserStar(int toolId, int currentRating)
        {
            // Resetear la configuración inicial
            SetupInitialData();

            var existingUserStar = new UserStar
            {
                Id = 1,
                Email = UserEmail,
                Type = nameof(MarketplaceTool),
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
            MockDb.Reset();
            SetupInitialData();
        }
    }
}

