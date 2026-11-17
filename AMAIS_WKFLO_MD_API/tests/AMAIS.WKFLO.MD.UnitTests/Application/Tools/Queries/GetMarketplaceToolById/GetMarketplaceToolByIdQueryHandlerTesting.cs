using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetMarketplaceToolById;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetMarketplaceToolById
{
    public class GetMarketplaceToolByIdQueryHandlerTesting
    {
        public GetMarketplaceToolByIdQueryHandler GetMarketplaceToolByIdQueryHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int MarketplaceToolId { get; private set; } = RandomNumber.Next();
        public int UnratedMarketplaceToolId { get; private set; } = RandomNumber.Next();

        public GetMarketplaceToolByIdQueryHandlerTesting()
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
                CreatedOn = fixedDate
            };

            var marketplaceTool2 = new MarketplaceTool()
            {
                Id = UnratedMarketplaceToolId,
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type",
                Config = "{}",
                CreatedOn = fixedDate
            };

            var marketplaceTools = new List<MarketplaceTool>() {
                marketplaceTool, marketplaceTool2
            }.BuildMockDbSet();

            // One live install of `marketplaceTool`, none for marketplaceTool2 - exercises the
            // live-computed Installations count (see GetMarketplaceToolByIdQueryHandler).
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

            // A single rating of 4 for `marketplaceTool`, none for marketplaceTool2 - exercises the
            // live-computed Stars average (see GetMarketplaceToolByIdQueryHandler): one rating => that
            // rating; no ratings => 0.
            var userStars = new List<UserStar>()
            {
                new UserStar { Email = "user1@example.com", Type = nameof(MarketplaceTool), TypeId = MarketplaceToolId, Value = 4, IsDeleted = false },
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceTools).Returns(marketplaceTools.Object);
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.UserStars).Returns(userStars.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            GetMarketplaceToolByIdQueryHandler = new GetMarketplaceToolByIdQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}

