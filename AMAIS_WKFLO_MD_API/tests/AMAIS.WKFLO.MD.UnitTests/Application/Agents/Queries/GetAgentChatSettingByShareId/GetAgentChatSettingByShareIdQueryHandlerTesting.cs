using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetAgentChatSettingByShareId;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetAgentChatSettingByShareId
{
    public class GetAgentChatSettingByShareIdQueryHandlerTesting
    {
        public GetAgentChatSettingByShareIdQueryHandler GetAgentChatSettingByShareIdQueryHandler { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public string ShareIdWithMaisyTag { get; private set; } = Guid.NewGuid().ToString();
        public string ShareIdWithoutMaisyTag { get; private set; } = Guid.NewGuid().ToString();
        public int AgentIdWithMaisyTag { get; private set; } = RandomNumber.Next();
        public int AgentIdWithoutMaisyTag { get; private set; } = RandomNumber.Next();

        public GetAgentChatSettingByShareIdQueryHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var tenant = new Tenant { Id = 1, Name = "TestTenant" };

            var agentWithTag = new PublishedTenantAgent
            {
                Id = AgentIdWithMaisyTag,
                Name = "Agent With Tag",
                Description = "Test Description",
                AgentTools = new List<AgentTool>(),
                IsActive = true,
                IsDeleted = false,
                Tenant = tenant
            };

            var agentWithoutTag = new PublishedTenantAgent
            {
                Id = AgentIdWithoutMaisyTag,
                Name = "Agent Without Tag",
                Description = "Test Description",
                AgentTools = new List<AgentTool>(),
                IsActive = true,
                IsDeleted = false,
                Tenant = tenant
            };

            var chatSettingWithTag = new AgentChatSetting
            {
                Id = 1,
                ShareId = ShareIdWithMaisyTag,
                AgentId = agentWithTag.Id,
                Agent = agentWithTag,
                IsShareable = true,
                IsDeleted = false
            };

            var chatSettingWithoutTag = new AgentChatSetting
            {
                Id = 2,
                ShareId = ShareIdWithoutMaisyTag,
                AgentId = agentWithoutTag.Id,
                Agent = agentWithoutTag,
                IsShareable = true,
                IsDeleted = false
            };

            var agentChatSettings = new List<AgentChatSetting>() { chatSettingWithTag, chatSettingWithoutTag }.BuildMockDbSet();

            var entityTag = new EntityTag
            {
                TagId = 1,
                EntityType = TaggableEntityTypes.Agent,
                EntityId = agentWithTag.Id,
                Tag = new Tag { Id = 1, Name = FeatureTagConsts.Maisy, TenantId = tenant.Id }
            };
            var entityTags = new List<EntityTag>() { entityTag }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(agentChatSettings.Object);
            db.Setup(x => x.EntityTags).Returns(entityTags.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            GetAgentChatSettingByShareIdQueryHandler = new GetAgentChatSettingByShareIdQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
