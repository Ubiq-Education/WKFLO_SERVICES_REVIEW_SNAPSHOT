using AMAIS.WKFLO.AI.Application.Agents.Commands.RateMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Events.MessageFeedbackRated;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.RateMessagePublicAgentByShareId
{
    public class RateMessagePublicAgentByShareIdCommandHandlerTests
    {
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly Mock<IChatStoreService> _chatStoreServiceMock;
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly TenantContext _tenantContext;
        private readonly RateMessagePublicAgentByShareIdCommandHandler _handler;

        private const string TenantName = "TestTenant";
        private const string ShareId = "share-001";
        private const string SessionId = "session-001";
        private const string MessageId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        private const string UserGuid = "user-guid-001";
        private const string SubjectId = "subj-001";

        public RateMessagePublicAgentByShareIdCommandHandlerTests()
        {
            _tenantContext = new TenantContext { TenantName = TenantName };
            var chatContext = new ChatContext { SubjectId = SubjectId };
            var userContext = new UserContext { Guid = UserGuid };

            _dbMock = new Mock<IMasterDataDbContext>();
            _chatStoreServiceMock = new Mock<IChatStoreService>();
            _mediatorMock = new Mock<IWkfloMediator>();
            _dbMock.Setup(x => x.SaveChangesAsync(default)).ReturnsAsync(1);
            // Default: no classification row exists for the rated message — most tests don't care about topic
            // correlation, only the two dedicated tests below do.
            _dbMock.Setup(x => x.MessageTopicClassifications).Returns(new List<MessageTopicClassification>().BuildMockDbSet().Object);

            _handler = new RateMessagePublicAgentByShareIdCommandHandler(
                _dbMock.Object,
                _chatStoreServiceMock.Object,
                _tenantContext,
                chatContext,
                userContext,
                _mediatorMock.Object);
        }

        [Fact]
        public async Task Handle_ThumbsUp_SetsCountersFromServiceResult()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };
            var conversation = new Conversation { AgentId = agent.Id, SessionId = SessionId, TotalThumbsUp = 0, TotalThumbsDown = 0 };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation> { conversation }.BuildMockDbSet().Object);
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, true))
                .ReturnsAsync((true, 1, 0));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            Assert.Equal(1, conversation.TotalThumbsUp);
            Assert.Equal(0, conversation.TotalThumbsDown);

            _mediatorMock.Verify(x => x.Publish(
                It.Is<MessageFeedbackRatedDomainEvent>(e =>
                    e.TenantName == TenantName
                    && e.SessionId == SessionId
                    && e.SubjectId == SubjectId
                    && e.PublishedTenantAgentId == agent.Id
                    && e.TenantAgentId == null
                    && e.MessageId == MessageId
                    && e.IsThumbsUp),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ThumbsDown_SetsCountersFromServiceResult()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };
            var conversation = new Conversation { AgentId = agent.Id, SessionId = SessionId, TotalThumbsUp = 0, TotalThumbsDown = 0 };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation> { conversation }.BuildMockDbSet().Object);
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, false))
                .ReturnsAsync((true, 0, 1));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = false }
            };

            // Act
            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            Assert.Equal(0, conversation.TotalThumbsUp);
            Assert.Equal(1, conversation.TotalThumbsDown);
        }

        [Fact]
        public async Task Handle_DuplicateRating_CountersReflectServiceResult()
        {
            // Arrange — service already counted the dedup; returns the same totals
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };
            var conversation = new Conversation { AgentId = agent.Id, SessionId = SessionId, TotalThumbsUp = 1, TotalThumbsDown = 0 };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation> { conversation }.BuildMockDbSet().Object);
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, true))
                .ReturnsAsync((true, 1, 0));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert — counter stays at 1, not incremented to 2
            Assert.IsType<SuccessResult<bool>>(result);
            Assert.Equal(1, conversation.TotalThumbsUp);
            Assert.Equal(0, conversation.TotalThumbsDown);
        }

        [Fact]
        public async Task Handle_MessageWasClassified_ThreadsTopicAndDatesThroughDomainEvent()
        {
            // Arrange — the classification row's MessageId is the QUESTION (a different guid from the rated
            // message); AnswerMessageId is what the lookup must match against, since a rating always targets
            // the assistant's answer.
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };
            var conversationDate = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
            var conversation = new Conversation { AgentId = agent.Id, SessionId = SessionId, ConversationDate = conversationDate };
            var messageDate = new DateTimeOffset(2026, 1, 5, 9, 55, 0, TimeSpan.Zero);
            var topicId = Guid.NewGuid();
            var classification = new MessageTopicClassification
            {
                MessageId = Guid.NewGuid(),
                AnswerMessageId = Guid.Parse(MessageId),
                TopicId = topicId,
                TopicName = "Billing",
                MessageDate = messageDate,
                SessionId = SessionId,
                AgentId = agent.Id,
                TopicVersion = "topic-version-6"
            };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation> { conversation }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.MessageTopicClassifications).Returns(new List<MessageTopicClassification> { classification }.BuildMockDbSet().Object);
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, true))
                .ReturnsAsync((true, 1, 0));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            _mediatorMock.Verify(x => x.Publish(
                It.Is<MessageFeedbackRatedDomainEvent>(e =>
                    e.TopicId == topicId
                    && e.TopicName == "Billing"
                    && e.MessageDate == messageDate
                    && e.ConversationDate == conversationDate
                    && e.TopicVersion == "topic-version-6"),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_MessageNeverClassified_TopicFieldsOmittedGracefully()
        {
            // Arrange — no MessageTopicClassification row for this message; classification may never have run.
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };
            var conversation = new Conversation { AgentId = agent.Id, SessionId = SessionId, ConversationDate = null };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation> { conversation }.BuildMockDbSet().Object);
            // MessageTopicClassifications already defaults to empty in the constructor.
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, true))
                .ReturnsAsync((true, 1, 0));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert — no throw, event still published with null topic/date fields
            Assert.IsType<SuccessResult<bool>>(result);
            _mediatorMock.Verify(x => x.Publish(
                It.Is<MessageFeedbackRatedDomainEvent>(e =>
                    e.TopicId == null && e.TopicName == null && e.MessageDate == null && e.ConversationDate == null
                    && e.TopicVersion == null),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ClassificationRowBelongsToDifferentSession_IsNotPulledIn()
        {
            // Regression guard: the lookup must be scoped to this agent/session, not just AnswerMessageId — a
            // caller passing an arbitrary/guessed MessageId (ChatStoreService.RateMessageAsync doesn't verify
            // the MessageId belongs to the blob being rated) must not be able to pull another conversation's —
            // potentially another tenant's — TopicId/TopicName/MessageDate into this event.
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };
            var conversation = new Conversation { AgentId = agent.Id, SessionId = SessionId, ConversationDate = null };
            var foreignClassification = new MessageTopicClassification
            {
                MessageId = Guid.NewGuid(),
                AnswerMessageId = Guid.Parse(MessageId),
                TopicId = Guid.NewGuid(),
                TopicName = "SomeoneElsesTopic",
                MessageDate = DateTimeOffset.UtcNow,
                SessionId = "a-different-session",
                AgentId = agent.Id
            };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation> { conversation }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.MessageTopicClassifications).Returns(new List<MessageTopicClassification> { foreignClassification }.BuildMockDbSet().Object);
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, true))
                .ReturnsAsync((true, 1, 0));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            _mediatorMock.Verify(x => x.Publish(
                It.Is<MessageFeedbackRatedDomainEvent>(e =>
                    e.TopicId == null && e.TopicName == null && e.MessageDate == null),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_TestChatConversation_SkipsClassificationLookupEntirely()
        {
            // SendAsync drops test-chat events unconditionally, so spending the classification lookup for them
            // is wasted work — must be skipped, not merely unused.
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };
            var conversation = new Conversation { AgentId = agent.Id, SessionId = SessionId, IsTestChat = true };
            var classification = new MessageTopicClassification
            {
                MessageId = Guid.NewGuid(),
                AnswerMessageId = Guid.Parse(MessageId),
                TopicId = Guid.NewGuid(),
                TopicName = "Billing",
                MessageDate = DateTimeOffset.UtcNow,
                SessionId = SessionId,
                AgentId = agent.Id
            };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation> { conversation }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.MessageTopicClassifications).Returns(new List<MessageTopicClassification> { classification }.BuildMockDbSet().Object);
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, true))
                .ReturnsAsync((true, 1, 0));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert — the DbSet is never even touched for a test chat.
            _dbMock.Verify(x => x.MessageTopicClassifications, Times.Never);
            _mediatorMock.Verify(x => x.Publish(
                It.Is<MessageFeedbackRatedDomainEvent>(e => e.TopicId == null && e.TopicName == null && e.MessageDate == null),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_AgentNotFound_ReturnsNotFoundResult()
        {
            // Arrange
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = "non-existent-share-id",
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            _chatStoreServiceMock.Verify(x => x.RateMessageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task Handle_AnswerMessageIdNotYetPersisted_FallsBackToChatHistoryLookup()
        {
            // Regression guard (Copilot PR review finding): MessageResolutionWorkflow persists
            // AnswerMessageId asynchronously, AFTER the response has already been returned to the visitor -
            // a fast rating can genuinely land before that write completes. The classification row already
            // exists (keyed by the QUESTION's own MessageId) but AnswerMessageId is still null, so the
            // primary lookup misses. The fallback must walk the persisted chat history backward from the
            // rated answer to find its question, then look the row up by that instead.
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };
            var conversationDate = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
            var conversation = new Conversation { AgentId = agent.Id, SessionId = SessionId, ConversationDate = conversationDate };
            var messageDate = new DateTimeOffset(2026, 1, 5, 9, 55, 0, TimeSpan.Zero);
            var topicId = Guid.NewGuid();
            var questionMessageId = Guid.NewGuid();
            var classification = new MessageTopicClassification
            {
                MessageId = questionMessageId,
                AnswerMessageId = null, // not yet set - resolution workflow hasn't run
                TopicId = topicId,
                TopicName = "Billing",
                MessageDate = messageDate,
                SessionId = SessionId,
                AgentId = agent.Id,
                TopicVersion = "topic-version-6"
            };

            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "Can I get a refund?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = questionMessageId.ToString() }));
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Yes, within 30 days.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = MessageId }));

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.Conversations).Returns(new List<Conversation> { conversation }.BuildMockDbSet().Object);
            _dbMock.Setup(x => x.MessageTopicClassifications).Returns(new List<MessageTopicClassification> { classification }.BuildMockDbSet().Object);
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, true))
                .ReturnsAsync((true, 1, 0));
            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, agent.Id, SessionId, UserGuid))
                .ReturnsAsync((DateTimeOffset.UtcNow, "Test Chat", chatHistory, new Dictionary<string, string>()));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            _mediatorMock.Verify(x => x.Publish(
                It.Is<MessageFeedbackRatedDomainEvent>(e =>
                    e.TopicId == topicId
                    && e.TopicName == "Billing"
                    && e.MessageDate == messageDate
                    && e.TopicVersion == "topic-version-6"),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_SessionNotFoundInBlob_ReturnsNotFoundResult()
        {
            // Arrange
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new PublishedTenantAgent { Id = 10, IsDeleted = false, Tenant = tenant };
            var chatSetting = new AgentChatSetting { ShareId = ShareId, IsDeleted = false, Agent = agent, AgentId = agent.Id };

            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting> { chatSetting }.BuildMockDbSet().Object);
            _chatStoreServiceMock
                .Setup(x => x.RateMessageAsync(TenantName, agent.Id, SessionId, UserGuid, MessageId, true))
                .ReturnsAsync((false, 0, 0));

            var command = new RateMessagePublicAgentByShareIdCommand
            {
                ShareId = ShareId,
                SessionId = SessionId,
                Input = new RateMessagePublicAgentInput { MessageId = MessageId, IsThumbsUp = true }
            };

            // Act
            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }
    }
}
