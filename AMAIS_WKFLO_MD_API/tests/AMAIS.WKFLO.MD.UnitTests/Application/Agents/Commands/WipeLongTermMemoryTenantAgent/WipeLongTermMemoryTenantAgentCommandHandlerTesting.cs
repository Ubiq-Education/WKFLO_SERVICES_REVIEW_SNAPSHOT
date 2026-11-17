using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Entities;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.WipeLongTermMemoryTenantAgent;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.WipeLongTermMemoryTenantAgent
{
    public class WipeLongTermMemoryTenantAgentCommandHandlerTesting
    {
        public WipeLongTermMemoryTenantAgentCommandHandler Handler { get; private set; }
        public Mock<IVectorStorageProvider> MockVectorStorageProvider { get; private set; }
        public int ActiveAgentId { get; } = 1;
        public string TenantName { get; } = "Test";

        public WipeLongTermMemoryTenantAgentCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };
            var tenant = new Tenant { Id = 1, Name = TenantName };

            // Two distinct users, one repeated to verify Distinct() works
            var conversations = new List<Conversation>
            {
                new Conversation { Id = 1, AgentId = ActiveAgentId, TenantId = tenant.Id, UserId = "user-1" },
                new Conversation { Id = 2, AgentId = ActiveAgentId, TenantId = tenant.Id, UserId = "user-2" },
                new Conversation { Id = 3, AgentId = ActiveAgentId, TenantId = tenant.Id, UserId = "user-1" }
            }.BuildMockDbSet();

            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();
            var publishedTenantAgents = new List<PublishedTenantAgent>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Conversations).Returns(conversations.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(publishedTenantAgents.Object);

            MockVectorStorageProvider = new Mock<IVectorStorageProvider>();
            MockVectorStorageProvider
                .Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<Expression<Func<IVectorData, bool>>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Handler = new WipeLongTermMemoryTenantAgentCommandHandler(db.Object, MockVectorStorageProvider.Object, tenantContext);
        }
    }
}
