using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Queries.ListMarketplaceTools;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.ListMarketplaceTools
{
    public class ListMarketplaceToolsQueryHandlerTesting
    {
        public ListMarketplaceToolsQueryHandler ListMarketplaceToolsQueryHandler { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }

        public int MarketplaceToolId { get; private set; } = RandomNumber.Next();

        public ListMarketplaceToolsQueryHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var marketplaceTool = new MarketplaceTool()
            {
                Id = MarketplaceToolId,
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "{}",
                CreatedOn = fixedDate,
            };

            var marketplaceTool2 = new MarketplaceTool()
            {
                Id = RandomNumber.Next(),
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type",
                Config = "{}",
                CreatedOn = fixedDate,
            };

            var marketplaceTools = new List<MarketplaceTool>() {
                marketplaceTool, marketplaceTool2
            }.BuildMockDbSet();

            // One live install of `marketplaceTool`, none for marketplaceTool2 - exercises the
            // live-computed Installations count (see ListMarketplaceToolsQueryHandler).
            var tenantTools = new List<TenantTool>()
            {
                new TenantTool
                {
                    Id = RandomNumber.Next(),
                    Name = "Installed Copy",
                    Description = "d",
                    Type = "API",
                    Config = "{}",
                    TenantId = 1,
                    InstalledFromMarketplaceToolId = MarketplaceToolId,
                    CreatedOn = fixedDate
                }
            }.BuildMockDbSet();

            // Star ratings for `marketplaceTool`: 2 and 4 (average 3, no rounding ambiguity), plus a
            // soft-deleted rating of 5 that must be excluded from the average - exercises the
            // live-computed Stars average (see ListMarketplaceToolsQueryHandler). `marketplaceTool2`
            // has no ratings at all and should default to 0.
            var userStars = new List<UserStar>()
            {
                new UserStar { Email = "user1@example.com", Type = nameof(MarketplaceTool), TypeId = MarketplaceToolId, Value = 2, IsDeleted = false },
                new UserStar { Email = "user2@example.com", Type = nameof(MarketplaceTool), TypeId = MarketplaceToolId, Value = 4, IsDeleted = false },
                new UserStar { Email = "user3@example.com", Type = nameof(MarketplaceTool), TypeId = MarketplaceToolId, Value = 5, IsDeleted = true },
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceTools).Returns(marketplaceTools.Object);
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.UserStars).Returns(userStars.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            ListMarketplaceToolsQueryHandler = new ListMarketplaceToolsQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
