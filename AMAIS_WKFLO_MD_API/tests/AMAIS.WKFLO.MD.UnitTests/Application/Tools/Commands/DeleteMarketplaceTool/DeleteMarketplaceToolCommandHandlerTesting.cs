using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteMarketplaceTool;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.DeleteMarketplaceTool
{
    public class DeleteMarketplaceToolCommandHandlerTesting
    {
        public DeleteMarketplaceToolCommandHandler DeleteMarketplaceToolCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int MarketplaceToolId { get; set; } = 1;

        public DeleteMarketplaceToolCommandHandlerTesting()
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
                SourcePublishedToolId = 1
            };

            var marketplaceTools = new List<MarketplaceTool>() { marketplaceTool }.BuildMockDbSet();

            var publishedTenantTool = new PublishedTenantTool()
            {
                Id = 1,
                MarketplaceToolId = MarketplaceToolId
            };

            var publishedTenantTools = new List<PublishedTenantTool>() { publishedTenantTool }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceTools).Returns(marketplaceTools.Object);
            db.Setup(x => x.PublishedTenantTools).Returns(publishedTenantTools.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            DeleteMarketplaceToolCommandHandler = new DeleteMarketplaceToolCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}

