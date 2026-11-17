using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.MD.Application.Tags.Queries.ListEntitiesByTag;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tags.Queries.ListEntitiesByTag
{
    public class ListEntitiesByTagQueryHandlerTesting
    {
        public ListEntitiesByTagQueryHandler ListEntitiesByTagQueryHandler { get; set; }
        public int TagId { get; set; } = 1;

        public int LiveAgentId { get; set; } = 10;
        public int DeadAgentId { get; set; } = 20;
        public int LiveToolId { get; set; } = 30;
        public int DeadToolId { get; set; } = 40;
        public int LiveAIModelId { get; set; } = 50;
        public int DeadAIModelId { get; set; } = 60;
        public int LiveAgentIdWithLowercaseEntityType { get; set; } = 70;

        public ListEntitiesByTagQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant() { Id = 1, Name = tenantContext.TenantName };

            var tag = new Tag() { Id = TagId, Name = "Maisy", TenantId = 1, Tenant = tenant };

            var mapper = WkfloMapperExtensions.CreateMapper();

            // EntityTag rows for both a live and a soft-deleted target, per taggable type. The handler
            // must never see the soft-deleted ones as still-existing.
            var entityTags = new List<EntityTag>()
            {
                new EntityTag { TagId = TagId, Tag = tag, EntityType = CommonConsts.TaggableEntityTypes.Agent, EntityId = LiveAgentId },
                new EntityTag { TagId = TagId, Tag = tag, EntityType = CommonConsts.TaggableEntityTypes.Agent, EntityId = DeadAgentId },
                new EntityTag { TagId = TagId, Tag = tag, EntityType = CommonConsts.TaggableEntityTypes.Tool, EntityId = LiveToolId },
                new EntityTag { TagId = TagId, Tag = tag, EntityType = CommonConsts.TaggableEntityTypes.Tool, EntityId = DeadToolId },
                new EntityTag { TagId = TagId, Tag = tag, EntityType = CommonConsts.TaggableEntityTypes.AIModel, EntityId = LiveAIModelId },
                new EntityTag { TagId = TagId, Tag = tag, EntityType = CommonConsts.TaggableEntityTypes.AIModel, EntityId = DeadAIModelId },
                // AssignTagToEntityCommandValidator validates EntityType case-insensitively and
                // AssignTagToEntityCommandHandler stores it verbatim, so a legitimately-tagged row can have
                // any casing. The liveness filter must not silently drop this one just because of casing.
                new EntityTag { TagId = TagId, Tag = tag, EntityType = "agent", EntityId = LiveAgentIdWithLowercaseEntityType }
            };

            // MockQueryable.Moq doesn't simulate EF Core's global `!IsDeleted` query filter (that's applied
            // by the real query provider, not by a plain in-memory LINQ mock) — so a soft-deleted row is
            // represented here simply by NOT seeding it into the "live" DbSet, exactly as it would be
            // invisible to a real filtered query against MasterDataDbContext.
            var agents = new List<Agent>()
            {
                new Agent { Id = LiveAgentId, AgentType = "TenantAgent", Name = "Live Agent" },
                new Agent { Id = LiveAgentIdWithLowercaseEntityType, AgentType = "TenantAgent", Name = "Live Agent (lowercase tag)" }
            }.BuildMockDbSet();

            var tenantTools = new List<TenantTool>()
            {
                new TenantTool { Id = LiveToolId, ToolType = "TenantTool", Name = "Live Tool" }
            }.BuildMockDbSet();
            var marketplaceTools = new List<MarketplaceTool>().BuildMockDbSet();
            var publishedTenantTools = new List<PublishedTenantTool>().BuildMockDbSet();

            var aiModels = new List<AIModel>()
            {
                new AIModel { Id = LiveAIModelId, Name = "Live AI Model" }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.EntityTags).Returns(entityTags.BuildMockDbSet().Object);
            db.Setup(x => x.Agents).Returns(agents.Object);
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.MarketplaceTools).Returns(marketplaceTools.Object);
            db.Setup(x => x.PublishedTenantTools).Returns(publishedTenantTools.Object);
            db.Setup(x => x.AIModels).Returns(aiModels.Object);

            ListEntitiesByTagQueryHandler = new ListEntitiesByTagQueryHandler(db.Object, mapper, tenantContext);
        }
    }
}
