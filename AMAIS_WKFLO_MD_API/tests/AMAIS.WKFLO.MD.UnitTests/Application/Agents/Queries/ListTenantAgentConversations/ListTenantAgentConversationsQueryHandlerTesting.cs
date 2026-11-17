using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgentConversations;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.ListTenantAgentConversations
{
    public class ListTenantAgentConversationsQueryHandlerTesting
    {
        public ListTenantAgentConversationsQueryHandler ListTenantAgentConversationsQueryHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }

        public ListTenantAgentConversationsQueryHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = "Test" };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var conversations = new List<Conversation>
            {
                new Conversation { Id = 1, AgentId = 1, TenantId = 1, SessionId = "s1", Name = "Chat 1", CreatedOn = fixedDate.AddMinutes(-1) },
                new Conversation { Id = 2, AgentId = 1, TenantId = 1, SessionId = "s2", Name = "Chat 2", CreatedOn = fixedDate.AddMinutes(-2) },
                new Conversation { Id = 3, AgentId = 1, TenantId = 1, SessionId = "s3", Name = "Chat 3", CreatedOn = fixedDate.AddMinutes(-3) },
                new Conversation { Id = 4, AgentId = 1, TenantId = 1, SessionId = "s4", Name = "Chat 4", CreatedOn = fixedDate.AddMinutes(-4) },
                new Conversation { Id = 5, AgentId = 1, TenantId = 1, SessionId = "s5", Name = "Chat 5", CreatedOn = fixedDate.AddMinutes(-5) },
                new Conversation { Id = 6, AgentId = 1, TenantId = 1, SessionId = "s6-test", Name = "Test Chat", CreatedOn = fixedDate.AddMinutes(-6), IsTestChat = true },
            }.BuildMockDbSet();

            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Conversations).Returns(conversations.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);

            ListTenantAgentConversationsQueryHandler = new ListTenantAgentConversationsQueryHandler(
                db.Object,
                mapper,
                tenantContext
            );
        }
    }
}


