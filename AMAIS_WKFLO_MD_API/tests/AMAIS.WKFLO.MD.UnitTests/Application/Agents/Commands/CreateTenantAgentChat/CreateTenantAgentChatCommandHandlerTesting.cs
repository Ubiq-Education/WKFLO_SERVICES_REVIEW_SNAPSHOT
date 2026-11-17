using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgentChat;
using AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Threading;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.CreateTenantAgentChat
{
    public class CreateTenantAgentChatCommandHandlerTesting
    {
        public CreateTenantAgentChatCommandHandler CreateTenantAgentChatCommandHandler { get; set; }
        public Mock<IWkfloMediator> MediatorMock { get; set; }

        public const string SubjectId = "test-subject-id";

        public CreateTenantAgentChatCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };
            var chatContext = new ChatContext() { SubjectId = SubjectId };
            var userContext = new UserContext() { Guid = "test-user-guid" };

            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            var DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var chatStoreService = new Mock<IChatStoreService>();
            chatStoreService
                .Setup(x => x.SaveChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ChatHistory>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()))
                .ReturnsAsync((0, 0));

            var mediator = new Mock<IWkfloMediator>();
            mediator
                .Setup(x => x.Send(It.IsAny<SyncConversationCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SuccessResult<bool>(true));
            MediatorMock = mediator;

            CreateTenantAgentChatCommandHandler = new CreateTenantAgentChatCommandHandler(
                mapper,
                chatStoreService.Object,
                tenantContext,
                chatContext,
                userContext,
                mediator.Object,
                DateTimeProviderMock.Object
            );
        }
    }
}

