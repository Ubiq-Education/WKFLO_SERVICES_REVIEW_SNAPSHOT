using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentConversations;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.ListPublicAgentConversations
{
    public class ListPublicAgentConversationsQueryHandlerTests
    {
        private const string TenantName = "test-tenant";
        private const string ShareId = "maisy-share-id";
        private const string CallerGuid = "11111111-1111-1111-1111-111111111111";
        private const string OtherUserGuid = "22222222-2222-2222-2222-222222222222";
        private const int TenantId = 1;
        private const int MaisyId = 10;
        private const int PagesAgentId = 20;
        private const int GalleryAgentId = 30;

        private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<IMasterDataDbContext> _dbMock = new();
        private readonly List<Conversation> _conversations = [];
        private readonly List<EntityTag> _entityTags = [];
        private readonly List<Agent> _agents = [];
        private readonly List<AgentChatSetting> _chatSettings = [];
        private readonly UserContext _userContext = new() { Guid = CallerGuid };

        public ListPublicAgentConversationsQueryHandlerTests()
        {
            var tenant = new Tenant { Id = TenantId, Name = TenantName };

            var maisy = new PublishedTenantAgent { Id = MaisyId, Name = "MAISY", Description = "Your AI assistant", TenantId = TenantId, Tenant = tenant, IconUrl = "https://icons/maisy-agent.png" };
            _agents.Add(maisy);
            _agents.Add(new PublishedTenantAgent { Id = PagesAgentId, Name = "Pages Agent", Description = "Creates and edits pages", TenantId = TenantId, Tenant = tenant, IconUrl = "https://icons/pages-agent.png" });
            _agents.Add(new PublishedTenantAgent { Id = GalleryAgentId, Name = "Gallery Agent", Description = null, TenantId = TenantId, Tenant = tenant });

            _chatSettings.Add(new AgentChatSetting { Id = 1, ShareId = ShareId, AgentId = MaisyId, Agent = maisy, IsWidget = true, ChatbotIconPath = "https://icons/maisy-chat.png" });

            _entityTags.Add(new EntityTag { TagId = 1, EntityType = TaggableEntityTypes.Agent, EntityId = MaisyId, Tag = new Tag { Id = 1, Name = FeatureTagConsts.Maisy, TenantId = TenantId } });
        }

        private ListPublicAgentConversationsQueryHandler BuildHandler()
        {
            _dbMock.Setup(x => x.Conversations).Returns(_conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.EntityTags).Returns(_entityTags.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(_agents.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.AgentChatSettings).Returns(_chatSettings.BuildMockDbSet().Object);

            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            return new ListPublicAgentConversationsQueryHandler(_dbMock.Object, new TenantContext { TenantName = TenantName }, _userContext, dateTimeProvider.Object);
        }

        private Conversation AddRow(string sessionId, int agentId = MaisyId, string userId = CallerGuid, DateTimeOffset? lastActivityAt = null,
            DateTimeOffset? createdOn = null, string name = "Title", string previewLine = "Preview", bool isTestChat = false, int tenantId = TenantId, bool isDeleted = false)
        {
            var row = new Conversation
            {
                Id = _conversations.Count + 1,
                SessionId = sessionId,
                AgentId = agentId,
                UserId = userId,
                TenantId = tenantId,
                Name = name,
                PreviewLine = previewLine,
                IsTestChat = isTestChat,
                IsDeleted = isDeleted,
                CreatedOn = createdOn ?? Now.AddDays(-1),
                LastActivityAt = lastActivityAt ?? Now.AddDays(-1)
            };
            _conversations.Add(row);
            return row;
        }

        private async Task<List<PublicAgentConversationResult>> ListAsync()
        {
            var result = await BuildHandler().Handle(new ListPublicAgentConversationsQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);
            return Assert.IsType<SuccessResult<List<PublicAgentConversationResult>>>(result).Data;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public async Task Handle_AnonymousCaller_ReturnsEmpty_WithoutQueryingConversations(string userGuid)
        {
            // Every anonymous visitor shares Guid.Empty: listing "their" sessions would expose everyone's.
            AddRow("s1", userId: Guid.Empty.ToString());
            _userContext.Guid = userGuid;

            var items = await ListAsync();

            Assert.Empty(items);
            _dbMock.Verify(x => x.Conversations, Times.Never);
        }

        [Fact]
        public async Task Handle_AgentNotTaggedMaisy_ReturnsEmpty()
        {
            AddRow("s1");
            _entityTags.Clear();

            Assert.Empty(await ListAsync());
        }

        [Fact]
        public async Task Handle_UnknownShareId_ReturnsNotFound()
        {
            _chatSettings.Clear();

            var result = await BuildHandler().Handle(new ListPublicAgentConversationsQuery { ShareId = ShareId }, TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Handle_ReturnsOnlyTheCallersRealRecentMaisyConversations()
        {
            AddRow("mine");
            AddRow("other-user", userId: OtherUserGuid);
            AddRow("test-chat", isTestChat: true);
            AddRow("other-tenant", tenantId: 2);
            AddRow("deleted", isDeleted: true);
            AddRow("too-old", lastActivityAt: Now.AddDays(-91), createdOn: Now.AddDays(-120));
            AddRow("not-maisy", agentId: PagesAgentId);

            var items = await ListAsync();

            Assert.Equal(["mine"], items.Select(i => i.SessionId));
        }

        [Fact]
        public async Task Handle_OrdersByTheLatestActivityAcrossEveryAgentInTheSession()
        {
            AddRow("older-but-continued", lastActivityAt: Now.AddDays(-10));
            // A spun-up agent finished in that session after the newest MAISY activity elsewhere.
            AddRow("older-but-continued", agentId: PagesAgentId, lastActivityAt: Now.AddHours(-1));
            AddRow("newest-maisy-row", lastActivityAt: Now.AddHours(-2));
            AddRow("oldest", lastActivityAt: Now.AddDays(-30));

            var items = await ListAsync();

            Assert.Equal(["older-but-continued", "newest-maisy-row", "oldest"], items.Select(i => i.SessionId));
            Assert.Equal(Now.AddHours(-1), items[0].LastActivityAt);
        }

        [Fact]
        public async Task Handle_FallsBackToLastUpdatedOnThenCreatedOn_ForLastActivity()
        {
            var updated = AddRow("updated");
            updated.LastActivityAt = null;
            updated.LastUpdatedOn = Now.AddHours(-3);
            var created = AddRow("created", createdOn: Now.AddHours(-1));
            created.LastActivityAt = null;

            var items = await ListAsync();

            Assert.Equal(["created", "updated"], items.Select(i => i.SessionId));
        }

        [Fact]
        public async Task Handle_ReturnsAtMost50Conversations_TheNewestOnes()
        {
            for (var i = 0; i < 60; i++)
                AddRow($"s{i}", lastActivityAt: Now.AddMinutes(-i));

            var items = await ListAsync();

            Assert.Equal(ListPublicAgentConversationsQueryHandler.MaxConversations, items.Count);
            Assert.Equal("s0", items[0].SessionId);
            Assert.Equal("s49", items[^1].SessionId);
        }

        [Fact]
        public async Task Handle_TitleAndPreviewComeFromTheMaisyRow_EmptyPreviewIsNull()
        {
            AddRow("s1", name: "AI title", previewLine: "  ");
            AddRow("s1", agentId: PagesAgentId, name: "Sub-agent hand-off", previewLine: "Sub-agent trigger");

            var item = Assert.Single(await ListAsync());

            Assert.Equal("AI title", item.Title);
            Assert.Null(item.PreviewLine);
        }

        [Fact]
        public async Task Handle_ListsMaisyFirstThenAgentsInJoinOrder()
        {
            // MAISY's own row is created at the end of its turn, after the agents it spun up finished theirs.
            AddRow("s1", createdOn: Now.AddMinutes(-1));
            AddRow("s1", agentId: GalleryAgentId, createdOn: Now.AddMinutes(-5));
            AddRow("s1", agentId: PagesAgentId, createdOn: Now.AddMinutes(-10));

            var item = Assert.Single(await ListAsync());

            Assert.Equal([MaisyId, PagesAgentId, GalleryAgentId], item.Agents.Select(a => a.AgentId));
        }

        [Fact]
        public async Task Handle_MapsAgentNameRoleAndAvatar()
        {
            _agents.Add(new PublishedTenantAgent { Id = 40, Name = "Verbose Agent", Description = new string('d', 400), TenantId = TenantId });
            _chatSettings.Add(new AgentChatSetting { Id = 2, AgentId = PagesAgentId, IsWidget = false, ChatbotIconPath = "https://icons/pages-not-widget.png", CreatedOn = Now });
            _chatSettings.Add(new AgentChatSetting { Id = 3, AgentId = PagesAgentId, IsWidget = true, ChatbotIconPath = "https://icons/pages-widget.png", CreatedOn = Now.AddDays(-5) });
            AddRow("s1", createdOn: Now.AddMinutes(-1));
            AddRow("s1", agentId: PagesAgentId, createdOn: Now.AddMinutes(-4));
            AddRow("s1", agentId: GalleryAgentId, createdOn: Now.AddMinutes(-3));
            AddRow("s1", agentId: 40, createdOn: Now.AddMinutes(-2));

            var agents = Assert.Single(await ListAsync()).Agents;

            var maisy = agents[0];
            Assert.Equal("MAISY", maisy.Name);
            Assert.Equal("Your AI assistant", maisy.Role);
            Assert.Equal("https://icons/maisy-chat.png", maisy.AvatarUrl);

            // The chat setting in MAISY's (widget) context wins over a newer non-widget one.
            Assert.Equal("https://icons/pages-widget.png", agents[1].AvatarUrl);

            // No chat setting and no icon: the UI shows its default.
            Assert.Null(agents[2].AvatarUrl);
            Assert.Null(agents[2].Role);

            Assert.Equal(ListPublicAgentConversationsQueryHandler.MaxRoleLength, agents[3].Role.Length);
            Assert.EndsWith("…", agents[3].Role);
        }

        [Fact]
        public async Task Handle_AvatarFallsBackToTheAgentIcon()
        {
            _chatSettings[0].ChatbotIconPath = null;
            AddRow("s1");

            var maisy = Assert.Single(Assert.Single(await ListAsync()).Agents);

            Assert.Equal("https://icons/maisy-agent.png", maisy.AvatarUrl);
        }

        [Fact]
        public async Task Handle_DeletedAgentDropsOutOfTheAvatars()
        {
            AddRow("s1");
            AddRow("s1", agentId: 99);

            var item = Assert.Single(await ListAsync());

            Assert.Equal([MaisyId], item.Agents.Select(a => a.AgentId));
        }
    }
}
