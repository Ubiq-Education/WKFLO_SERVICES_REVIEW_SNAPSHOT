using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgentChat;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.DeleteTenantAgentChat
{
    public class DeleteTenantAgentChatCommandHandlerTesting
    {
        public DeleteTenantAgentChatCommandHandler Handler { get; private set; }
        public Mock<IChatStoreService> MockChatStoreService { get; private set; }
        public Mock<IChatDocumentStore> MockChatDocumentStore { get; private set; }
        public string TenantName { get; } = "Test";
        public string UserGuid { get; } = "00000000-0000-0000-0000-0000000000aa";

        public DeleteTenantAgentChatCommandHandlerTesting()
        {
            Initialize(chatExists: true);
        }

        public void Initialize(bool chatExists)
        {
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var conversations = new List<Conversation>
            {
                new Conversation { Id = 1, AgentId = 1, TenantId = tenant.Id, SessionId = "session-1", UserId = UserGuid, IsDeleted = false }
            };

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { tenant }.BuildMockDbSet().Object);
            db.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            MockChatStoreService = new Mock<IChatStoreService>();
            MockChatStoreService
                .Setup(x => x.DeleteChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(chatExists);

            MockChatDocumentStore = new Mock<IChatDocumentStore>();

            Handler = new DeleteTenantAgentChatCommandHandler(db.Object, MockChatStoreService.Object, new TenantContext { TenantName = TenantName },
                new UserContext { Guid = UserGuid }, MockChatDocumentStore.Object, Mock.Of<ILogger<DeleteTenantAgentChatCommandHandler>>());
        }
    }
}
