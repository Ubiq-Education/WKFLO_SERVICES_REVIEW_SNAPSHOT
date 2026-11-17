using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.WipeShortTermMemoryTenantAgent;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.WipeShortTermMemoryTenantAgent
{
    public class WipeShortTermMemoryTenantAgentCommandHandlerTesting
    {
        public WipeShortTermMemoryTenantAgentCommandHandler Handler { get; private set; }
        public Mock<IChatStoreService> MockChatStoreService { get; private set; }
        public Mock<IChatDocumentStore> MockChatDocumentStore { get; private set; }
        public int ActiveAgentId { get; } = 1;
        public int PublishedAgentId { get; } = 10;
        public string TenantName { get; } = "Test";

        private readonly Tenant _tenant = new Tenant { Id = 1, Name = "Test" };

        public WipeShortTermMemoryTenantAgentCommandHandlerTesting()
        {
            Initialize(withPublishedAgent: false);
        }

        private void Initialize(bool withPublishedAgent)
        {
            var tenantContext = new TenantContext { TenantName = TenantName };

            // PublishedTenantAgents linked back to the TenantAgent via CreatedByTenantAgentId
            var publishedAgents = withPublishedAgent
                ? new List<PublishedTenantAgent>
                {
                    new PublishedTenantAgent { Id = PublishedAgentId, TenantId = _tenant.Id, CreatedByTenantAgentId = ActiveAgentId }
                }
                : new List<PublishedTenantAgent>();

            var publishedAgentsMock = publishedAgents.BuildMockDbSet();

            // Conversations for the TenantAgent, plus one for the PublishedTenantAgent when applicable
            var conversations = new List<Conversation>
            {
                new Conversation { Id = 1, AgentId = ActiveAgentId, TenantId = _tenant.Id, SessionId = "session-1", UserId = "user-1", IsDeleted = false },
                new Conversation { Id = 2, AgentId = ActiveAgentId, TenantId = _tenant.Id, SessionId = "session-2", UserId = "user-2", IsDeleted = false }
            };

            if (withPublishedAgent)
                conversations.Add(new Conversation { Id = 3, AgentId = PublishedAgentId, TenantId = _tenant.Id, SessionId = "session-3", UserId = "user-3", IsDeleted = false });

            var conversationsMock = conversations.BuildMockDbSet();
            var tenants = new List<Tenant> { _tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Conversations).Returns(conversationsMock.Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(publishedAgentsMock.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            MockChatStoreService = new Mock<IChatStoreService>();
            MockChatStoreService
                .Setup(x => x.DeleteChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            MockChatDocumentStore = new Mock<IChatDocumentStore>();

            Handler = new WipeShortTermMemoryTenantAgentCommandHandler(db.Object, MockChatStoreService.Object, tenantContext, MockChatDocumentStore.Object,
                Mock.Of<ILogger<WipeShortTermMemoryTenantAgentCommandHandler>>());
        }

        public void SetupWithPublishedAgent() => Initialize(withPublishedAgent: true);

        public void Reset() => Initialize(withPublishedAgent: false);
    }
}
