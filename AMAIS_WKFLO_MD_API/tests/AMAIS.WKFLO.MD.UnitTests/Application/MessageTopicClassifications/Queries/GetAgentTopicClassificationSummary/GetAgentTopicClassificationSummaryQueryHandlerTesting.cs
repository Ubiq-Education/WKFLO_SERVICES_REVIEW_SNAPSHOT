using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Queries.GetAgentTopicClassificationSummary;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.MessageTopicClassifications.Queries.GetAgentTopicClassificationSummary
{
    public class GetAgentTopicClassificationSummaryQueryHandlerTesting
    {
        public GetAgentTopicClassificationSummaryQueryHandler Handler { get; set; }

        public GetAgentTopicClassificationSummaryQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var agents = new List<Agent>
            {
                new TenantAgent { Id = 1, Tenant = tenant }
            }.BuildMockDbSet();

            var classifications = new List<MessageTopicClassification>
            {
                new MessageTopicClassification { Id = 1, AgentId = 1, TopicId = Guid.NewGuid(), TopicName = "Billing", SessionId = "session-1", MessageId = Guid.NewGuid(), MessageDate = DateTimeOffset.UtcNow.AddDays(-5), IsDeleted = false },
                new MessageTopicClassification { Id = 2, AgentId = 1, TopicId = Guid.NewGuid(), TopicName = "Billing", SessionId = "session-2", MessageId = Guid.NewGuid(), MessageDate = DateTimeOffset.UtcNow.AddDays(-10), IsDeleted = false },
                new MessageTopicClassification { Id = 3, AgentId = 1, TopicId = Guid.NewGuid(), TopicName = "Support", SessionId = "session-3", MessageId = Guid.NewGuid(), MessageDate = DateTimeOffset.UtcNow.AddDays(-15), IsDeleted = false },
                new MessageTopicClassification { Id = 4, AgentId = 1, TopicId = Guid.NewGuid(), TopicName = "FAQ", SessionId = "session-4", MessageId = Guid.NewGuid(), MessageDate = DateTimeOffset.UtcNow.AddDays(-100), IsDeleted = false },
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(agents.Object);
            db.Setup(x => x.MessageTopicClassifications).Returns(classifications.Object);

            Handler = new GetAgentTopicClassificationSummaryQueryHandler(db.Object, tenantContext);
        }
    }
}
