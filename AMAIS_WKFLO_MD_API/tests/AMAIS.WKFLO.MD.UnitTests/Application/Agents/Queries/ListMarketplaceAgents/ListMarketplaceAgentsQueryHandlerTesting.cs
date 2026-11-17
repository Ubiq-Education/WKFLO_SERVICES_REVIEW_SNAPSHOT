using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListMarketplaceAgents;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.ListMarketplaceAgents
{
    public class ListMarketplaceAgentsQueryHandlerTesting
    {
        public ListMarketplaceAgentsQueryHandler ListMarketplaceAgentsQueryHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int MarketplaceAgentId { get; private set; } = RandomNumber.Next();
        public int TaggedMarketplaceAgentId { get; private set; } = RandomNumber.Next();
        public int TagId { get; private set; } = RandomNumber.Next();

        public const string TagName = "AMAIS";

        public ListMarketplaceAgentsQueryHandlerTesting()
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
                IconUrl = "https://static.example.com/test-agent-icon.png",
            };

            var marketplaceAgent2 = new MarketplaceAgent()
            {
                Id = RandomNumber.Next(),
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2",
                AgentTools = new List<AgentTool>(),
                CreatedOn = fixedDate,
            };

            // Belongs to a different tenant than whichever tenant happens to be "current" for other
            // tests — proves the tag filter deliberately ignores tenant scoping (see
            // ListMarketplaceAgentsQuery.Tag doc comment): Marketplace agents are a global catalog.
            var taggedMarketplaceAgent = new MarketplaceAgent()
            {
                Id = TaggedMarketplaceAgentId,
                Name = "Curated Template Agent",
                Description = "Curated Template Description",
                SystemPrompt = "Curated Template System Prompt",
                AgentTools = new List<AgentTool>(),
                CreatedOn = fixedDate,
            };

            var marketplaceAgents = new List<MarketplaceAgent>() {
                marketplaceAgent, marketplaceAgent2, taggedMarketplaceAgent
            }.BuildMockDbSet();

            var otherTenant = new Tenant { Id = 1, Name = "OtherTenant" };
            var tag = new Tag { Id = TagId, Name = TagName, TenantId = otherTenant.Id, Tenant = otherTenant };

            var entityTags = new List<EntityTag>()
            {
                new EntityTag { TagId = TagId, Tag = tag, EntityType = CommonConsts.TaggableEntityTypes.Agent, EntityId = TaggedMarketplaceAgentId },
            }.BuildMockDbSet();

            // One live install of `marketplaceAgent` for a tenant, none for the other two - exercises the
            // live-computed Installations count (see ListMarketplaceAgentsQueryHandler).
            var tenantAgents = new List<TenantAgent>()
            {
                new TenantAgent
                {
                    Id = RandomNumber.Next(),
                    Name = "Test Name",
                    Description = "Test Description",
                    SystemPrompt = "Test System Prompt",
                    TenantId = otherTenant.Id,
                    InstalledFromMarketplaceAgentId = MarketplaceAgentId,
                    CreatedOn = fixedDate,
                }
            }.BuildMockDbSet();

            // Star ratings for `marketplaceAgent`: 4 and 5 (average 4.5, rounds away-from-zero to 5),
            // plus a soft-deleted rating of 1 that must be excluded from the average - exercises the
            // live-computed Stars average (see ListMarketplaceAgentsQueryHandler). `marketplaceAgent2`
            // and `taggedMarketplaceAgent` have no ratings at all and should default to 0.
            var userStars = new List<UserStar>()
            {
                new UserStar { Email = "user1@example.com", Type = nameof(MarketplaceAgent), TypeId = MarketplaceAgentId, Value = 4, IsDeleted = false },
                new UserStar { Email = "user2@example.com", Type = nameof(MarketplaceAgent), TypeId = MarketplaceAgentId, Value = 5, IsDeleted = false },
                new UserStar { Email = "user3@example.com", Type = nameof(MarketplaceAgent), TypeId = MarketplaceAgentId, Value = 1, IsDeleted = true },
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceAgents).Returns(marketplaceAgents.Object);
            db.Setup(x => x.EntityTags).Returns(entityTags.Object);
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);
            db.Setup(x => x.UserStars).Returns(userStars.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            ListMarketplaceAgentsQueryHandler = new ListMarketplaceAgentsQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}


