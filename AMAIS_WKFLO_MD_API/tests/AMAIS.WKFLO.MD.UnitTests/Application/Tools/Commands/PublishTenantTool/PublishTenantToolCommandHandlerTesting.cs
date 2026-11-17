using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Commands.PublishTenantTool;
using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.PublishTenantTool
{
    public class PublishTenantToolCommandHandlerTesting
    {
        private const string TenantName = "Test";
        private static readonly DateTimeOffset FixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        /// <summary>The "now" returned by the mocked <see cref="IDateTimeProvider"/> the handler stamps
        /// LastUpdatedOn with — later than <see cref="FixedDate"/> so tests can distinguish "never touched"
        /// from "just republished".</summary>
        public static readonly DateTimeOffset PublishNowUtc = FixedDate.AddDays(1);

        public PublishTenantToolCommandHandler Handler { get; }

        /// <summary>The mocked PublishedTenantTool list — Handle() mutates entities already in it in place
        /// (e.g. a republish), so tests can inspect the resulting state directly for that case. A brand-new
        /// publish goes through AddAsync instead, which this mock doesn't reflect back into the list — use
        /// <see cref="PublishedToolsMock"/>'s Verify for that case instead.</summary>
        public List<PublishedTenantTool> PublishedTools { get; }

        public Mock<DbSet<PublishedTenantTool>> PublishedToolsMock { get; private set; }

        /// <summary>TenantTool.Id of an OAuth2 MCP tool that's already connected, never published before.</summary>
        public int ConnectedToolId { get; } = 1;

        public Guid ConnectedToolMcpOAuthConnectionId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

        /// <summary>TenantTool.Id of an OAuth2 MCP tool never connected, never published before.</summary>
        public int NotConnectedToolId { get; } = 2;

        /// <summary>TenantTool.Id of an already-connected tool being re-published; already has a PublishedTenantTool row.</summary>
        public int RepublishToolId { get; } = 3;

        public Guid RepublishToolMcpOAuthConnectionId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

        /// <summary>Id of the pre-existing PublishedTenantTool for <see cref="RepublishToolId"/>.</summary>
        public int ExistingPublishedToolId { get; } = 103;

        /// <summary>The <see cref="RepublishToolId"/> TenantTool itself, so tests can set its Config before publishing.</summary>
        public TenantTool RepublishTool { get; }

        /// <summary>TenantTool.Id of a tool being republished with NO actual field differences from its
        /// existing PublishedTenantTool copy — the scenario where Mapster's map produces zero real changes
        /// and EF's change tracker would otherwise leave the entity Unchanged.</summary>
        public int UnchangedRepublishToolId { get; } = 4;

        /// <summary>Id of the pre-existing, already-in-sync PublishedTenantTool for <see cref="UnchangedRepublishToolId"/>.</summary>
        public int UnchangedExistingPublishedToolId { get; } = 104;

        public PublishTenantToolCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };
            var tenant = new Tenant { Id = 1, Name = TenantName };

            var connectedTool = new TenantTool
            {
                Id = ConnectedToolId,
                Name = "Humantic MCP",
                Description = "Humantic MCP tool",
                Type = CommonToolTypeConsts.MCP,
                Config = "{}",
                McpOAuthConnectionId = ConnectedToolMcpOAuthConnectionId,
                Tenant = tenant,
                IsActive = true,
                CreatedOn = FixedDate
            };

            var notConnectedTool = new TenantTool
            {
                Id = NotConnectedToolId,
                Name = "Humantic MCP (not connected)",
                Description = "Humantic MCP tool never connected",
                Type = CommonToolTypeConsts.MCP,
                Config = "{}",
                McpOAuthConnectionId = null,
                Tenant = tenant,
                IsActive = true,
                CreatedOn = FixedDate
            };

            var republishTool = RepublishTool = new TenantTool
            {
                Id = RepublishToolId,
                Name = "Humantic MCP (republish)",
                Description = "Humantic MCP tool being republished",
                Type = CommonToolTypeConsts.MCP,
                Config = "{}",
                McpOAuthConnectionId = RepublishToolMcpOAuthConnectionId,
                Tenant = tenant,
                IsActive = true,
                CreatedOn = FixedDate
            };

            var existingPublishedTool = new PublishedTenantTool
            {
                Id = ExistingPublishedToolId,
                Name = republishTool.Name,
                Description = republishTool.Description,
                Type = republishTool.Type,
                Config = republishTool.Config,
                McpOAuthConnectionId = null, // stale — not yet synced from a republish
                Tenant = tenant,
                IsActive = true,
                CreatedByTenantToolId = RepublishToolId,
                CreatedOn = FixedDate
            };

            // A republish with NO actual field differences from its existing PublishedTenantTool copy — the
            // scenario that used to leave zero trace (no LastUpdatedOn bump, no audit row) because Mapster's
            // map produces zero real changes and EF's change tracker would otherwise leave the entity
            // Unchanged. See PublishTenantToolCommandHandler's explicit LastUpdatedOn stamp in that branch.
            var unchangedRepublishTool = new TenantTool
            {
                Id = UnchangedRepublishToolId,
                Name = "Already In Sync Tool",
                Description = "Nothing has changed since the last publish",
                Type = CommonToolTypeConsts.Http,
                Config = "{}",
                McpOAuthConnectionId = null,
                Tenant = tenant,
                IsActive = true,
                CreatedOn = FixedDate
            };

            var unchangedExistingPublishedTool = new PublishedTenantTool
            {
                Id = UnchangedExistingPublishedToolId,
                Name = unchangedRepublishTool.Name,
                Description = unchangedRepublishTool.Description,
                Type = unchangedRepublishTool.Type,
                Config = unchangedRepublishTool.Config,
                McpOAuthConnectionId = null,
                Tenant = tenant,
                IsActive = true,
                CreatedByTenantToolId = UnchangedRepublishToolId,
                CreatedOn = FixedDate
            };

            var tenantTools = new List<TenantTool> { connectedTool, notConnectedTool, republishTool, unchangedRepublishTool }.BuildMockDbSet();
            PublishedTools = new List<PublishedTenantTool> { existingPublishedTool, unchangedExistingPublishedTool };
            var publishedTools = PublishedTools.BuildMockDbSet();
            PublishedToolsMock = publishedTools;
            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();
            var entityTags = new List<EntityTag>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.PublishedTenantTools).Returns(publishedTools.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.EntityTags).Returns(entityTags.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            db.Setup(x => x.SaveChangesAsync(default)).ReturnsAsync(1);

            var mapper = WkfloMapperExtensions.CreateMapper();

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(PublishNowUtc);

            Handler = new PublishTenantToolCommandHandler(db.Object, mapper, tenantContext, dateTimeProviderMock.Object);
        }
    }
}
