using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteTenantTool;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.DeleteTenantTool
{
    public class DeleteTenantToolCommandHandlerTesting
    {
        public DeleteTenantToolCommandHandler DeleteTenantToolCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int TenantToolId { get; set; } = 1;
        public int TenantToolWithPublishedToolId { get; set; } = 3;
        public int PublishedTenantToolId { get; set; } = 4;
        public int UnrelatedTenantToolWithPublishedToolId { get; set; } = 5;
        public int UnrelatedPublishedTenantToolId { get; set; } = 6;

        // Kept as a direct reference so tests can assert the same object instance the handler mutated
        // (IsDeleted is a plain property write, visible regardless of DbSet mocking).
        public PublishedTenantTool PublishedTenantTool { get; set; }

        // A second, dedicated PublishedTenantTool that no test ever deletes — kept separate from
        // PublishedTenantTool above (which TestHandlerOk_CascadesToPublishedTool mutates) so that a test
        // asserting "an unrelated published tool was left untouched" is never coupled to what another test
        // in this IClassFixture-shared instance did or didn't do. xUnit shares one *Testing instance across
        // every [Fact] in the class and gives no ordering guarantee between them, so two tests must never
        // assert opposite expectations (IsDeleted true vs. false) about the very same shared object.
        public PublishedTenantTool UnrelatedPublishedTenantTool { get; set; }

        public DeleteTenantToolCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tenantTool = new TenantTool()
            {
                Id = TenantToolId,
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "{}",
                CreatedOn = fixedDate
            };

            var tenantToolWithPublishedTool = new TenantTool()
            {
                Id = TenantToolWithPublishedToolId,
                Name = "Test Name With Published Tool",
                Description = "Test Description",
                Type = "Test Type",
                Config = "{}",
                CreatedOn = fixedDate
            };

            var publishedTenantTool = new PublishedTenantTool()
            {
                Id = PublishedTenantToolId,
                CreatedByTenantToolId = TenantToolWithPublishedToolId,
                Name = "Test Name With Published Tool",
                Description = "Test Description",
                Type = "Test Type",
                Config = "{}",
                CreatedOn = fixedDate
            };

            PublishedTenantTool = publishedTenantTool;

            var unrelatedTenantToolWithPublishedTool = new TenantTool()
            {
                Id = UnrelatedTenantToolWithPublishedToolId,
                Name = "Test Name With Unrelated Published Tool",
                Description = "Test Description",
                Type = "Test Type",
                Config = "{}",
                CreatedOn = fixedDate
            };

            var unrelatedPublishedTenantTool = new PublishedTenantTool()
            {
                Id = UnrelatedPublishedTenantToolId,
                CreatedByTenantToolId = UnrelatedTenantToolWithPublishedToolId,
                Name = "Test Name With Unrelated Published Tool",
                Description = "Test Description",
                Type = "Test Type",
                Config = "{}",
                CreatedOn = fixedDate
            };

            UnrelatedPublishedTenantTool = unrelatedPublishedTenantTool;

            var tenantTools = new List<TenantTool>() { tenantTool, tenantToolWithPublishedTool, unrelatedTenantToolWithPublishedTool }.BuildMockDbSet();
            var publishedTenantTools = new List<PublishedTenantTool>() { publishedTenantTool, unrelatedPublishedTenantTool }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.PublishedTenantTools).Returns(publishedTenantTools.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            DeleteTenantToolCommandHandler = new DeleteTenantToolCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}

