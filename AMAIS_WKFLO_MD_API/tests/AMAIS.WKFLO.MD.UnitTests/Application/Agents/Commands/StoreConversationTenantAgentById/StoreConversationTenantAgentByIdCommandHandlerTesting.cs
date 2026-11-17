using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.StoreConversationTenantAgentById;
using AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.StoreConversationTenantAgentById
{
    public class StoreConversationTenantAgentByIdCommandHandlerTesting
    {
        public StoreConversationTenantAgentByIdCommandHandler Handler { get; private set; }
        public Mock<IMasterDataDbContext> MockDb { get; private set; }
        public Mock<IChatStoreService> MockChatStoreService { get; private set; }
        public Mock<IWkfloMediator> MockMediator { get; private set; }
        public TenantContext TenantContext { get; private set; }

        public int ActiveAgentId { get; } = 1;
        public string TenantName { get; } = "Test";
        public const string SubjectId = "test-subject-id";

        public StoreConversationTenantAgentByIdCommandHandlerTesting()
        {
            Initialize();
        }

        private void Initialize()
        {
            // Setup mocked DateTimeProvider
            var DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            TenantContext = new TenantContext { TenantName = TenantName };
            var chatContext = new ChatContext { SubjectId = SubjectId };

            MockDb = new Mock<IMasterDataDbContext>();
            MockChatStoreService = new Mock<IChatStoreService>();

            MockChatStoreService
                .Setup(x => x.SaveChatAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<ChatHistory>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()))
                .ReturnsAsync((0, 0));

            MockMediator = new Mock<IWkfloMediator>();
            MockMediator
                .Setup(x => x.Send(It.IsAny<SyncConversationCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SuccessResult<bool>(true));

            SetupInitialData();

            Handler = new StoreConversationTenantAgentByIdCommandHandler(
                MockDb.Object,
                MockChatStoreService.Object,
                TenantContext,
                chatContext,
                MockMediator.Object,
                DateTimeProviderMock.Object);
        }

        private void SetupInitialData()
        {
            var tenant = new Tenant { Id = 1, Name = TenantName };

            var agents = new List<PublishedTenantAgent>
            {
                new PublishedTenantAgent { Id = ActiveAgentId, IsDeleted = false, IsActive = true, TenantId = tenant.Id, Tenant = tenant }
            }.BuildMockDbSet();

            MockDb.Setup(x => x.PublishedTenantAgents).Returns(agents.Object);
        }

        public void SetupNonExistentAgent()
        {
            var emptyAgents = new List<PublishedTenantAgent>().BuildMockDbSet();
            MockDb.Setup(x => x.PublishedTenantAgents).Returns(emptyAgents.Object);
        }

        public void VerifySaveChatCalledOnce(string sessionId)
        {
            MockChatStoreService.Verify(
                x => x.SaveChatAsync(
                    TenantName,
                    ActiveAgentId,
                    sessionId,
                    It.IsAny<string>(),
                    It.IsAny<ChatHistory>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<string>()),
                Times.Once);
        }

        public void Reset()
        {
            Initialize();
        }
    }
}

