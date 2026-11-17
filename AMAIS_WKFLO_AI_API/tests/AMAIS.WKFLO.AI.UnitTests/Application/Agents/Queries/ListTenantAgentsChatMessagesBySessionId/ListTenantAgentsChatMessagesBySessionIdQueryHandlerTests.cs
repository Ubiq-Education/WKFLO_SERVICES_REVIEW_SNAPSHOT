using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.ListTenantAgentsChatMessagesBySessionId;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.ListTenantAgentsChatMessagesBySessionId
{
    public class ListTenantAgentsChatMessagesBySessionIdQueryHandlerTests
    {
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly Mock<IChatStoreService> _chatStoreServiceMock;
        private readonly ListTenantAgentsChatMessagesBySessionIdQueryHandler _handler;

        private const string TenantName = "TestTenant";
        private const string SessionId = "session-001";

        public ListTenantAgentsChatMessagesBySessionIdQueryHandlerTests()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            _dbMock = new Mock<IMasterDataDbContext>();
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            _chatStoreServiceMock = new Mock<IChatStoreService>();

            _handler = new ListTenantAgentsChatMessagesBySessionIdQueryHandler(
                _dbMock.Object,
                mapper,
                _chatStoreServiceMock.Object,
                new TenantContext { TenantName = TenantName },
                new UserContext { Guid = Guid.Empty.ToString() });
        }

        private static Tenant BuildTenant() => new() { Id = 1, Name = TenantName };

        [Fact]
        public async Task Handle_MultipleParticipatingAgents_ReturnsMergedMessagesSortedByTimestampWithAttribution()
        {
            var tenant = BuildTenant();
            var agentOne = new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant };
            var agentTwo = new TenantAgent { Id = 6, Name = "Billing Bot", Tenant = tenant };

            var conversations = new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = agentOne.Id, Tenant = tenant, TenantId = tenant.Id },
                new() { Id = 2, SessionId = SessionId, AgentId = agentTwo.Id, Tenant = tenant, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { agentOne, agentTwo }.BuildMockDbSet().Object);

            var laterHistory = new ChatHistory();
            laterHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Later message",
                metadata: new Dictionary<string, object>
                {
                    [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString(),
                    [CommonMetadataConsts.Timestamp] = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)
                }));

            var earlierHistory = new ChatHistory();
            earlierHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Earlier message",
                metadata: new Dictionary<string, object>
                {
                    [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString(),
                    [CommonMetadataConsts.Timestamp] = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero)
                }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentOne.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", laterHistory, (Dictionary<string, string>)null));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentTwo.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", earlierHistory, (Dictionary<string, string>)null));

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = agentOne.Id, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Equal(2, success.Data.Count);
            Assert.Equal("Earlier message", success.Data[0].Content);
            Assert.Equal(agentTwo.Id, success.Data[0].AgentId);
            Assert.Equal(agentTwo.Name, success.Data[0].AgentName);
            Assert.Equal("Later message", success.Data[1].Content);
            Assert.Equal(agentOne.Id, success.Data[1].AgentId);
            Assert.Equal(agentOne.Name, success.Data[1].AgentName);
        }

        [Fact]
        public async Task Handle_MultipleParticipatingAgents_StampsChatbotIconUrlPerAgentFromAgentChatSettings()
        {
            var tenant = BuildTenant();
            var agentOne = new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant };
            var agentTwo = new TenantAgent { Id = 6, Name = "Billing Bot", Tenant = tenant };

            var conversations = new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = agentOne.Id, Tenant = tenant, TenantId = tenant.Id },
                new() { Id = 2, SessionId = SessionId, AgentId = agentTwo.Id, Tenant = tenant, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { agentOne, agentTwo }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { Id = 1, AgentId = agentOne.Id, ChatbotIconPath = "https://cdn.test/refunds-icon.png" },
                new() { Id = 2, AgentId = agentTwo.Id, ChatbotIconPath = "https://cdn.test/billing-icon.png" }
            }.BuildMockDbSet().Object);

            var historyOne = new ChatHistory();
            historyOne.Add(new ChatMessageContent(AuthorRole.Assistant, "From agent one",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            var historyTwo = new ChatHistory();
            historyTwo.Add(new ChatMessageContent(AuthorRole.Assistant, "From agent two",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentOne.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", historyOne, (Dictionary<string, string>)null));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentTwo.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", historyTwo, (Dictionary<string, string>)null));

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = agentOne.Id, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Equal(2, success.Data.Count);
            Assert.Contains(success.Data, m => m.AgentId == agentOne.Id && m.ChatbotIconUrl == "https://cdn.test/refunds-icon.png");
            Assert.Contains(success.Data, m => m.AgentId == agentTwo.Id && m.ChatbotIconUrl == "https://cdn.test/billing-icon.png");
        }

        [Fact]
        public async Task Handle_RootAgentWithMultipleChatSettingRows_ResolvesFromMostRecentRootRowAndDoesNotThrow()
        {
            // Regression test: AgentId is NOT unique on AgentChatSetting (e.g. separate widget vs.
            // non-widget configs for the same agent). Building the icon lookup with ToDictionaryAsync
            // threw "An item with the same key has already been added" for agents with more than one
            // active row. The root's own IsWidget context comes from its most-recently-updated row —
            // here that's the NON-widget one, proving the resolution isn't just "always prefer widget".
            var tenant = BuildTenant();
            var agentOne = new TenantAgent { Id = 18018, Name = "Multi-Config Bot", Tenant = tenant };

            var conversations = new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = agentOne.Id, Tenant = tenant, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { agentOne }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { Id = 1, AgentId = agentOne.Id, IsWidget = true, ChatbotIconPath = "https://cdn.test/widget-icon.png", CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new() { Id = 2, AgentId = agentOne.Id, IsWidget = false, ChatbotIconPath = "https://cdn.test/non-widget-icon.png", CreatedOn = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero) }
            }.BuildMockDbSet().Object);

            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "Multi-config reply",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentOne.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", history, (Dictionary<string, string>)null));

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = agentOne.Id, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Single(success.Data);
            Assert.Equal("https://cdn.test/non-widget-icon.png", success.Data[0].ChatbotIconUrl);
        }

        [Fact]
        public async Task Handle_SubAgentIconResolvedInRootsNonWidgetContext_NotIndependentlyDefaultingToWidget()
        {
            // The widget/non-widget context for the whole request is a property of the root agent
            // (query.Id) — the one the caller is actually viewing — not something each participating
            // agent should guess independently. Root here is configured non-widget only; the
            // participating sub-agent has BOTH a widget and a non-widget row. Its icon must resolve
            // from its own non-widget row (matching the root's context), not its widget row.
            var tenant = BuildTenant();
            var rootAgent = new TenantAgent { Id = 5, Name = "Root Bot", Tenant = tenant };
            var subAgent = new TenantAgent { Id = 6, Name = "Sub Bot", Tenant = tenant };

            var conversations = new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = rootAgent.Id, Tenant = tenant, TenantId = tenant.Id },
                new() { Id = 2, SessionId = SessionId, AgentId = subAgent.Id, Tenant = tenant, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { rootAgent, subAgent }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { Id = 1, AgentId = rootAgent.Id, IsWidget = false, ChatbotIconPath = "https://cdn.test/root-icon.png" },
                new() { Id = 2, AgentId = subAgent.Id, IsWidget = true, ChatbotIconPath = "https://cdn.test/sub-widget-icon.png" },
                new() { Id = 3, AgentId = subAgent.Id, IsWidget = false, ChatbotIconPath = "https://cdn.test/sub-non-widget-icon.png" }
            }.BuildMockDbSet().Object);

            var rootHistory = new ChatHistory();
            rootHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Root reply",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            var subHistory = new ChatHistory();
            subHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Sub reply",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, rootAgent.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", rootHistory, (Dictionary<string, string>)null));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, subAgent.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", subHistory, (Dictionary<string, string>)null));

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = rootAgent.Id, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Equal(2, success.Data.Count);
            Assert.Contains(success.Data, m => m.AgentId == rootAgent.Id && m.ChatbotIconUrl == "https://cdn.test/root-icon.png");
            Assert.Contains(success.Data, m => m.AgentId == subAgent.Id && m.ChatbotIconUrl == "https://cdn.test/sub-non-widget-icon.png");
        }

        [Fact]
        public async Task Handle_AgentWithoutChatSetting_LeavesChatbotIconUrlNull()
        {
            var tenant = BuildTenant();
            var agentOne = new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant };

            var conversations = new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = agentOne.Id, Tenant = tenant, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { agentOne }.BuildMockDbSet().Object);
            // No AgentChatSettings row for this agent — icon should be left null, not throw.
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "No icon configured",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentOne.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", history, (Dictionary<string, string>)null));

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = agentOne.Id, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Single(success.Data);
            Assert.Null(success.Data[0].ChatbotIconUrl);
        }

        [Fact]
        public async Task Handle_NoConversationRows_ReturnsEmptyListWithoutLoadingChat()
        {
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation>().BuildMockDbSet().Object);

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = 1, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Empty(success.Data);
            _chatStoreServiceMock.Verify(x => x.LoadChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_RatingsApplied_PerAgentWithoutCrossAgentCollision()
        {
            var tenant = BuildTenant();
            var agentOne = new TenantAgent { Id = 5, Name = "Refunds Bot", Tenant = tenant };
            var agentTwo = new TenantAgent { Id = 6, Name = "Billing Bot", Tenant = tenant };

            var conversations = new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = agentOne.Id, Tenant = tenant, TenantId = tenant.Id },
                new() { Id = 2, SessionId = SessionId, AgentId = agentTwo.Id, Tenant = tenant, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { agentOne, agentTwo }.BuildMockDbSet().Object);

            var messageIdOne = Guid.NewGuid().ToString();
            var historyOne = new ChatHistory();
            historyOne.Add(new ChatMessageContent(AuthorRole.Assistant, "From agent one",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageIdOne }));

            var messageIdTwo = Guid.NewGuid().ToString();
            var historyTwo = new ChatHistory();
            historyTwo.Add(new ChatMessageContent(AuthorRole.Assistant, "From agent two",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageIdTwo }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentOne.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", historyOne, new Dictionary<string, string> { [messageIdOne] = "thumbsUp" }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agentTwo.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", historyTwo, new Dictionary<string, string> { [messageIdTwo] = "thumbsDown" }));

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = agentOne.Id, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Equal(2, success.Data.Count);
            Assert.Contains(success.Data, m => m.Content == "From agent one" && m.Rating == "thumbsUp");
            Assert.Contains(success.Data, m => m.Content == "From agent two" && m.Rating == "thumbsDown");
        }

        [Fact]
        public async Task Handle_SpinUpAgentUserMessage_IsStrippedButAssistantReplyKept()
        {
            var tenant = BuildTenant();
            var mainAgent = new TenantAgent { Id = 5, Name = "Main Bot", Tenant = tenant };
            var spinUpAgent = new TenantAgent { Id = 6, Name = "Spin-Up Bot", Tenant = tenant };

            var conversations = new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = mainAgent.Id, Tenant = tenant, TenantId = tenant.Id },
                new() { Id = 2, SessionId = SessionId, AgentId = spinUpAgent.Id, Tenant = tenant, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { mainAgent, spinUpAgent }.BuildMockDbSet().Object);

            var mainHistory = new ChatHistory();
            mainHistory.Add(new ChatMessageContent(AuthorRole.User, "User asks a question",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));
            mainHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Main agent reply",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            var spinUpHistory = new ChatHistory();
            spinUpHistory.Add(new ChatMessageContent(AuthorRole.User, "Internal hand-off trigger",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));
            spinUpHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Spin-up agent reply",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, mainAgent.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", mainHistory, (Dictionary<string, string>)null));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, spinUpAgent.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", spinUpHistory, (Dictionary<string, string>)null));

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = mainAgent.Id, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Equal(3, success.Data.Count);
            Assert.Contains(success.Data, m => m.Content == "User asks a question" && m.AgentId == mainAgent.Id);
            Assert.Contains(success.Data, m => m.Content == "Main agent reply" && m.AgentId == mainAgent.Id);
            Assert.Contains(success.Data, m => m.Content == "Spin-up agent reply" && m.AgentId == spinUpAgent.Id);
            Assert.DoesNotContain(success.Data, m => m.Content == "Internal hand-off trigger");
        }

        [Fact]
        public async Task Handle_SpinUpAgentGeneratedImage_RenderAsSystemUserMessage_IsKept()
        {
            // ImageGenerationPlugin.BuildImageMessage puts a spun-up agent's generated/edited images
            // under AuthorRole.User (Anthropic rejects an image content block inside an assistant
            // turn), flagged RenderAsSystem — that's a plugin-authored record, not something the human
            // actually typed, so it must survive the same strip that removes the genuine hand-off
            // trigger message below.
            var tenant = BuildTenant();
            var mainAgent = new TenantAgent { Id = 5, Name = "MAISY", Tenant = tenant };
            var spinUpAgent = new TenantAgent { Id = 6, Name = "Vera", Tenant = tenant };

            var conversations = new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = mainAgent.Id, Tenant = tenant, TenantId = tenant.Id },
                new() { Id = 2, SessionId = SessionId, AgentId = spinUpAgent.Id, Tenant = tenant, TenantId = tenant.Id }
            };
            _dbMock.Setup(x => x.Conversations).Returns(conversations.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { mainAgent, spinUpAgent }.BuildMockDbSet().Object);

            var mainHistory = new ChatHistory();
            mainHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "MAISY reply",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));

            var spinUpHistory = new ChatHistory();
            spinUpHistory.Add(new ChatMessageContent(AuthorRole.User, "Internal hand-off trigger",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));
            spinUpHistory.Add(new ChatMessageContent(AuthorRole.User, "FileName: 'edited.png'",
                metadata: new Dictionary<string, object>
                {
                    [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString(),
                    [CommonMetadataConsts.RenderAsSystem] = true
                }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, mainAgent.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", mainHistory, (Dictionary<string, string>)null));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, spinUpAgent.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", spinUpHistory, (Dictionary<string, string>)null));

            var query = new ListTenantAgentsChatMessagesBySessionIdQuery { Id = mainAgent.Id, SessionId = SessionId };

            var result = await _handler.Handle(query, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Equal(2, success.Data.Count);
            Assert.Contains(success.Data, m => m.Content == "MAISY reply" && m.AgentId == mainAgent.Id);
            Assert.Contains(success.Data, m => m.Content == "FileName: 'edited.png'" && m.AgentId == spinUpAgent.Id);
            Assert.DoesNotContain(success.Data, m => m.Content == "Internal hand-off trigger");
        }

        /// <summary>
        /// US 7502: MAISY and Kit were both stopped by the same Stop, so each saved its partial text and its own "Stopped"
        /// marker. The merged history keeps both partial texts but shows ONE divider, after them, with the marker keys.
        /// </summary>
        [Fact]
        public async Task Handle_SameStopSavedByTwoAgents_ShowsOneDividerWithMarkerMetadata()
        {
            var tenant = BuildTenant();
            var maisy = new TenantAgent { Id = 5, Name = "MAISY", Tenant = tenant };
            var kit = new TenantAgent { Id = 6, Name = "Kit", Tenant = tenant };

            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation>
            {
                new() { Id = 1, SessionId = SessionId, AgentId = maisy.Id, Tenant = tenant, TenantId = tenant.Id },
                new() { Id = 2, SessionId = SessionId, AgentId = kit.Id, Tenant = tenant, TenantId = tenant.Id }
            }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Agents).Returns(new List<Agent> { maisy, kit }.BuildMockDbSet().Object);

            var stoppedAt = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
            var maisyHistory = RoundTrip(new ChatHistory
            {
                new ChatMessageContent(AuthorRole.Assistant, "MAISY partial", metadata: new Dictionary<string, object>
                {
                    [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString(),
                    [CommonMetadataConsts.Timestamp] = stoppedAt.AddSeconds(1)
                }),
                StoppedTurnMarker.Build("maisy-marker", stoppedAt.AddSeconds(1).AddMilliseconds(1), stoppedAt)
            });
            var kitHistory = RoundTrip(new ChatHistory
            {
                new ChatMessageContent(AuthorRole.Assistant, "Kit partial", metadata: new Dictionary<string, object>
                {
                    [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString(),
                    [CommonMetadataConsts.Timestamp] = stoppedAt.AddSeconds(2)
                }),
                StoppedTurnMarker.Build("kit-marker", stoppedAt.AddSeconds(2).AddMilliseconds(1), stoppedAt)
            });

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, maisy.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", maisyHistory, (Dictionary<string, string>)null));
            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, kit.Id, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Session 1", kitHistory, (Dictionary<string, string>)null));

            var result = await _handler.Handle(new ListTenantAgentsChatMessagesBySessionIdQuery { Id = maisy.Id, SessionId = SessionId }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<List<TenantAgentChatMessageResult>>>(result);
            Assert.Equal(new[] { "MAISY partial", "Kit partial", StoppedTurnMarker.Text }, success.Data.Select(m => m.Content));

            var divider = success.Data[2];
            Assert.Equal("kit-marker", divider.MessageId);
            Assert.Equal("assistant", divider.Type);
            Assert.Equal(ChannelMessageType.Metadata, divider.ChannelMessageType);
            Assert.Equal(true, divider.Metadata[CommonMetadataConsts.IsStoppedMarker]);
            Assert.Equal("2026-10-19T10:00:00.0000000+00:00", divider.Metadata[CommonMetadataConsts.StoppedAt]);
            Assert.True(divider.Metadata.ContainsKey(CommonMetadataConsts.RenderAsSystem));
        }

        private static ChatHistory RoundTrip(ChatHistory history) =>
            JsonSerializer.Deserialize<ChatHistory>(JsonSerializer.Serialize(history));
    }
}
