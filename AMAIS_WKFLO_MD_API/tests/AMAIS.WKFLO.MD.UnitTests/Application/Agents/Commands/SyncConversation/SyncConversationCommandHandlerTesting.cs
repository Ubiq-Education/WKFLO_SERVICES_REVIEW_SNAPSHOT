using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Agents.Commands.SyncConversation;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.SyncConversation
{
    public class SyncConversationCommandHandlerTesting
    {
        public SyncConversationCommandHandler SyncConversationCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public List<Conversation> Conversations { get; set; }
        public Conversation ExistingConversation { get; set; }

        public const string ExistingTenantName = "Test";
        public const string ExistingSessionId = "existing-session";
        public const int AgentId = 1;

        public SyncConversationCommandHandlerTesting()
        {
            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tenant = new Tenant { Id = 1, Name = ExistingTenantName };

            ExistingConversation = new Conversation
            {
                Id = 1,
                AgentId = AgentId,
                TenantId = tenant.Id,
                SessionId = ExistingSessionId,
                Name = "Old Name",
                CreatedOn = fixedDate.AddDays(-1)
            };

            Conversations = new List<Conversation> { ExistingConversation };

            var conversationsDbSet = Conversations.BuildMockDbSet();
            conversationsDbSet
                .Setup(x => x.AddAsync(It.IsAny<Conversation>(), It.IsAny<CancellationToken>()))
                .Callback<Conversation, CancellationToken>((c, _) => Conversations.Add(c));

            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Conversations).Returns(conversationsDbSet.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            SyncConversationCommandHandler = new SyncConversationCommandHandler(db.Object, DateTimeProviderMock.Object);
        }
    }
}
