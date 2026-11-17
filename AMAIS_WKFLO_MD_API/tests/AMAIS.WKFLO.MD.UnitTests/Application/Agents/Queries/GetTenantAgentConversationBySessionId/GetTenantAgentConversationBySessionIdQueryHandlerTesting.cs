using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentConversationBySessionId;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetTenantAgentConversationBySessionId
{
    public class GetTenantAgentConversationBySessionIdQueryHandlerTesting
    {
        public static readonly DateTimeOffset ResolvedActivityAt = new DateTimeOffset(2026, 1, 1, 11, 30, 0, TimeSpan.Zero);

        public GetTenantAgentConversationBySessionIdQueryHandler GetTenantAgentConversationBySessionIdQueryHandler { get; set; }

        public GetTenantAgentConversationBySessionIdQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            var DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var conversations = new List<Conversation>
            {
                new Conversation { Id = 1, AgentId = 1, TenantId = 1, SessionId = "session-abc", Name = "Chat 1", UserId = "user-guid-1", CreatedOn = DateTimeProviderMock.Object.DateTimeOffsetUtcNow() },
                new Conversation { Id = 2, AgentId = 1, TenantId = 1, SessionId = "session-xyz", Name = "Chat 2", UserId = "user-guid-1", CreatedOn = DateTimeProviderMock.Object.DateTimeOffsetUtcNow(), IsResolved = false, LastActivityAt = ResolvedActivityAt },
                new Conversation { Id = 3, AgentId = 2, TenantId = 1, SessionId = "session-abc", Name = "Chat 3", UserId = "user-guid-2", CreatedOn = DateTimeProviderMock.Object.DateTimeOffsetUtcNow() },
            }.BuildMockDbSet();

            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Conversations).Returns(conversations.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);

            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("Hello");
            chatHistory.AddAssistantMessage("Hi there!");

            var chatStoreService = new Mock<IChatStoreService>();
            chatStoreService
                .Setup(x => x.LoadChatAsync(tenantContext.TenantName, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.FromResult((DateTimeProviderMock.Object.DateTimeOffsetUtcNow(), "Chat 1", chatHistory, (Dictionary<string, string>)null)));

            GetTenantAgentConversationBySessionIdQueryHandler = new GetTenantAgentConversationBySessionIdQueryHandler(
                db.Object,
                mapper,
                tenantContext,
                chatStoreService.Object
            );
        }
    }
}
