using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetChatClassificationTopicByAgentId;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetChatClassificationTopicByAgentId
{
    public class GetChatClassificationTopicByAgentIdQueryHandlerTesting
    {
        public GetChatClassificationTopicByAgentIdQueryHandler Handler { get; set; }

        public readonly Guid ActiveTopicId = Guid.NewGuid();
        public readonly Guid InactiveTopicId = Guid.NewGuid();

        public GetChatClassificationTopicByAgentIdQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var agentWithTool = new TenantAgent { Id = 1, Tenant = tenant };
            var agentWithMalformedTool = new TenantAgent { Id = 4, Tenant = tenant };

            // Agent Id 2 intentionally has no linked Chat_Classification tool at all — used to
            // exercise the "tool not found" branch.

            var configJson = JsonConvert.SerializeObject(new
            {
                topics = new[]
                {
                    new { id = ActiveTopicId, name = "Billing", description = "Billing questions", isActive = true, handoffMessage = "Transferring you to billing." },
                    new { id = InactiveTopicId, name = "Retired Topic", description = "No longer used", isActive = false, handoffMessage = (string)null }
                },
                version = 3
            });

            var chatClassificationTool = new TenantTool
            {
                Id = 10,
                Type = "Chat_Classification",
                Config = configJson,
                Tenant = tenant,
                CreatedOn = DateTimeOffset.UtcNow.AddDays(-30),
                LastUpdatedOn = DateTimeOffset.UtcNow.AddDays(-1),
                AgentTools = new List<AgentTool>
                {
                    new AgentTool { AgentId = agentWithTool.Id, Agent = agentWithTool, ToolId = 10 }
                }
            };

            var malformedConfigTool = new TenantTool
            {
                Id = 11,
                Type = "Chat_Classification",
                Config = "not valid json",
                Tenant = tenant,
                CreatedOn = DateTimeOffset.UtcNow,
                AgentTools = new List<AgentTool>
                {
                    new AgentTool { AgentId = agentWithMalformedTool.Id, Agent = agentWithMalformedTool, ToolId = 11 }
                }
            };

            var tenantTools = new List<TenantTool> { chatClassificationTool, malformedConfigTool }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);

            Handler = new GetChatClassificationTopicByAgentIdQueryHandler(db.Object, tenantContext);
        }
    }
}
