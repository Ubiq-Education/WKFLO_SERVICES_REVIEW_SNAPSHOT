using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Worker.Application.Jobs.TopicClassification;
using AMAIS.WKFLO.Worker.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Worker.UnitTests.Jobs.TopicClassification
{
    public class TopicClassificationJobHandlerTests
    {
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly Mock<IChatStoreService> _chatStoreServiceMock;
        private readonly Mock<IDateTimeProvider> _dateTimeProviderMock;
        private readonly Mock<IDxpChatbotAnalyticsService> _dxpChatbotAnalyticsServiceMock;
        private readonly TopicClassificationJobHandler _handler;
        private readonly List<MessageTopicClassification> _persistedClassifications = new();

        private const string TenantName = "TestTenant";
        private const int AgentId = 42;
        private const string SessionId = "session-abc";
        private const string TopicVersion = "topic-version-1";
        private const string SubjectId = "subj-abc";

        private static readonly Guid ActiveTopicId = Guid.NewGuid();
        private static readonly DateTimeOffset FixedDate = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public TopicClassificationJobHandlerTests()
        {
            _dbMock = new Mock<IMasterDataDbContext>();
            _chatStoreServiceMock = new Mock<IChatStoreService>();
            _dateTimeProviderMock = new Mock<IDateTimeProvider>();
            _dxpChatbotAnalyticsServiceMock = new Mock<IDxpChatbotAnalyticsService>();
            _dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(FixedDate);

            var classificationsDbSet = _persistedClassifications.BuildMockDbSet();
            classificationsDbSet.Setup(x => x.Add(It.IsAny<MessageTopicClassification>()))
                .Callback<MessageTopicClassification>(m => _persistedClassifications.Add(m));
            classificationsDbSet.Setup(x => x.Remove(It.IsAny<MessageTopicClassification>()))
                .Callback<MessageTopicClassification>(m => _persistedClassifications.Remove(m));

            _dbMock.Setup(x => x.MessageTopicClassifications).Returns(classificationsDbSet.Object);
            _dbMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            _dxpChatbotAnalyticsServiceMock
                .Setup(x => x.ReportMessageFeedbackAsync(
                    It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            _handler = new TopicClassificationJobHandler(
                _dbMock.Object,
                _chatStoreServiceMock.Object,
                _dateTimeProviderMock.Object,
                Options.Create(new WorkerSettings()),
                Mock.Of<ILogger<TopicClassificationJobHandler>>(),
                _dxpChatbotAnalyticsServiceMock.Object);
        }

        // Empty SessionId/SubjectId on the template mirrors the real BuildDxpContextTemplateAsync — both are
        // filled in per conversation by ProcessConversationAsync via a record `with` expression, from
        // conversation.SessionId/SubjectId.
        private static DxpChatbotEventContext BuildDxpContext() => new(
            DxpTenantName: "dxp-tenant",
            Domain: "https://dxp.example.com",
            ApiKey: "api-key-123",
            TenantId: 1,
            TenantName: TenantName,
            SessionId: string.Empty,
            SubjectId: string.Empty,
            TenantAgentId: AgentId,
            PublishedTenantAgentId: null,
            ShareId: null);

        private static List<ClassificationTopic> GetActiveTopics() =>
        [
            new ClassificationTopic { Id = ActiveTopicId, Name = "Billing", Description = "Billing questions", IsActive = true, HandoffMessage = "Routing to billing" }
        ];

        private static Conversation GetConversation() => new()
        {
            SessionId = SessionId,
            AgentId = AgentId,
            UserId = Guid.Empty.ToString(),
            SubjectId = SubjectId,
            ConversationDate = FixedDate
        };

        private static Mock<IChatCompletionService> BuildChatCompletionServiceMock(string rawResponse)
        {
            var mock = new Mock<IChatCompletionService>();
            IReadOnlyList<ChatMessageContent> response = [new ChatMessageContent(AuthorRole.Assistant, rawResponse)];

            mock.Setup(x => x.GetChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            return mock;
        }

        [Theory]
        [InlineData("NONE")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-guid")]
        public async Task ProcessConversationAsync_NoMatch_PersistsUncategorisedRow(string rawResponse)
        {
            // Arrange
            var messageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "What's the weather?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(rawResponse);
            var dxpContext = BuildDxpContext();

            // Act
            var (_, _, messages) = await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            // Assert
            Assert.Equal(1, messages);
            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(messageId, persisted.MessageId);
            Assert.Null(persisted.TopicId);
            Assert.Null(persisted.TopicName);
            Assert.Equal(AgentId, persisted.AgentId);
            Assert.Equal(SessionId, persisted.SessionId);
            // US 7142: a classification's version doesn't depend on whether it matched a topic — the
            // "uncategorised" row still gets TopicVersion set.
            Assert.Equal(TopicVersion, persisted.TopicVersion);
            _dbMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.Is<DxpChatbotEventContext>(c => c.SessionId == SessionId && c.SubjectId == SubjectId),
                messageId.ToString(), null, null, TopicVersion, FixedDate, FixedDate), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_UnknownTopicId_PersistsUncategorisedRow()
        {
            // Arrange: a syntactically valid GUID that doesn't match any active topic
            var messageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "Some message",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(Guid.NewGuid().ToString());

            // Act: pass a null DXP context (no active integration) — must not throw and must not report
            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, null, TopicVersion, CancellationToken.None);

            // Assert
            var persisted = Assert.Single(_persistedClassifications);
            Assert.Null(persisted.TopicId);
            Assert.Null(persisted.TopicName);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        /// <summary>Bug 8189: an attached document (millions of characters) is never classified as a question.</summary>
        [Fact]
        public async Task ProcessConversationAsync_AttachedDocumentMessages_AreNotClassifiedAsQuestions()
        {
            var attachmentId = Guid.NewGuid();
            var questionId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User,
                [new TextContent("Document content:\nMILLIONS OF CHARACTERS", metadata: new Dictionary<string, object> { [CommonMetadataConsts.FileName] = "big.pdf" })],
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = attachmentId.ToString() }));
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = questionId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, BuildDxpContext(), TopicVersion, CancellationToken.None);

            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(questionId, persisted.MessageId);
            chatCompletionServiceMock.Verify(x => x.GetChatMessageContentsAsync(
                It.Is<ChatHistory>(h => h.Any(m => m.Content != null && m.Content.Contains("MILLIONS OF CHARACTERS"))),
                It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ProcessConversationAsync_MatchedTopic_PersistsClassifiedRow()
        {
            // Arrange
            var messageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            // Act
            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            // Assert
            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(ActiveTopicId, persisted.TopicId);
            Assert.Equal("Billing", persisted.TopicName);
            // US 7142: the row's TopicVersion must be persisted, same as the value already reported to DXP.
            Assert.Equal(TopicVersion, persisted.TopicVersion);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.Is<DxpChatbotEventContext>(c => c.SessionId == SessionId && c.SubjectId == SubjectId),
                messageId.ToString(), ActiveTopicId, "Billing", TopicVersion, FixedDate, FixedDate), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_ConversationHasNoConversationDate_ReportsNullConversationDate()
        {
            // Arrange — the free-lunch ConversationDate comes straight off the Conversation entity already
            // being iterated; when it's null (not yet set), that must flow through as null, not throw.
            var messageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();
            var conversationWithNoDate = new Conversation
            {
                SessionId = SessionId,
                AgentId = AgentId,
                UserId = Guid.Empty.ToString(),
                SubjectId = SubjectId,
                ConversationDate = null
            };

            // Act
            await _handler.ProcessConversationAsync(
                conversationWithNoDate, TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            // Assert
            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), messageId.ToString(), ActiveTopicId, "Billing", TopicVersion, FixedDate, null), Times.Once);
        }

        // ==================== US 7142: feedback re-report (self-healing Satisfaction rate) ====================

        [Fact]
        public async Task ProcessConversationAsync_MessageHasExistingRating_ReReportsFeedbackWithCurrentTopicAndVersion()
        {
            // Arrange — a message that already has a rating (from IChatStoreService.LoadChatAsync's 4th
            // return value, MessageRatings) must trigger a ReportMessageFeedbackAsync re-report with the
            // CURRENT topic/version from this reclassification, same as the existing ReportMessageTopicAsync
            // re-report loop already does for Questions/Resolved rate. A rating is always keyed by the
            // ANSWER's own MessageId, never the question's — the assistant reply below has its own distinct id.
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Pay online via the portal.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerMessageId.ToString() }));

            var messageRatings = new Dictionary<string, string> { [answerMessageId.ToString()] = "thumbsUp" };

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, messageRatings));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            // Act
            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            // Assert — reported with the ANSWER's own MessageId, matching RateMessage*CommandHandler's
            // own convention (the "Chatbot Feedback" event's MessageId is always the rated message itself).
            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.Is<DxpChatbotEventContext>(c => c.SessionId == SessionId && c.SubjectId == SubjectId),
                answerMessageId.ToString(), true, ActiveTopicId, "Billing", FixedDate, FixedDate, TopicVersion), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_MessageHasThumbsDownRating_ReReportsFeedbackAsNotThumbsUp()
        {
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Pay online via the portal.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerMessageId.ToString() }));

            var messageRatings = new Dictionary<string, string> { [answerMessageId.ToString()] = "thumbsDown" };

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, messageRatings));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), answerMessageId.ToString(), false, ActiveTopicId, "Billing", FixedDate, FixedDate, TopicVersion), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_RatingKeyedByQuestionId_NeverMatches_NoFeedbackReported()
        {
            // Guards the exact bug just fixed: messageRatings is ALWAYS keyed by the answer's own MessageId,
            // never the question's — a rating dictionary keyed by the question id (as if it were the rated
            // message) must never match and must never be re-reported.
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Pay online via the portal.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerMessageId.ToString() }));

            var messageRatings = new Dictionary<string, string> { [messageId.ToString()] = "thumbsUp" };

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, messageRatings));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task ProcessConversationAsync_RatingKeyedByPreservedAnswerMessageId_ReReportsFeedback_NoAssistantMessageNeededInTranscript()
        {
            // A message already judged (AnswerMessageId persisted from a prior resolution run) must be
            // findable via that PRESERVED id, without needing to re-extract it from the transcript.
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();

            _persistedClassifications.Add(new MessageTopicClassification
            {
                MessageId = messageId,
                SessionId = SessionId,
                AgentId = AgentId,
                TopicId = ActiveTopicId,
                TopicName = "Billing",
                MessageDate = FixedDate,
                TopicVersion = "old-version",
                IsResolved = true,
                AnswerMessageId = answerMessageId
            });

            // Deliberately only the question is in the transcript passed to LoadChatAsync — the answer's own
            // text isn't needed to find the rating, only its id, which comes from the preserved SQL row.
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            var messageRatings = new Dictionary<string, string> { [answerMessageId.ToString()] = "thumbsUp" };

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, messageRatings));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), answerMessageId.ToString(), true, ActiveTopicId, "Billing", FixedDate, FixedDate, TopicVersion), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_MessageHasNoRating_DoesNotReReportFeedback()
        {
            // A message with no entry in messageRatings must never trigger a feedback re-report.
            var messageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, new Dictionary<string, string>()));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>()),
                Times.Never);

            // The existing "Chatbot Message Topic" re-report is unaffected by the absence of a rating.
            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), messageId.ToString(), ActiveTopicId, "Billing", TopicVersion, FixedDate, FixedDate), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_NullMessageRatings_DoesNotThrow_AndDoesNotReReportFeedback()
        {
            // messageRatings can legitimately be null (a conversation with no ratings blob entry at all yet) —
            // must not throw, and the existing ReportMessageTopicAsync re-report must still behave exactly as
            // before.
            var messageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            var exception = await Record.ExceptionAsync(() => _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None));

            Assert.Null(exception);
            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string>()),
                Times.Never);
            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), messageId.ToString(), ActiveTopicId, "Billing", TopicVersion, FixedDate, FixedDate), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_UncategorisedMessageWithRating_ReReportsFeedbackWithNullTopic()
        {
            // A rated message that reclassifies as "uncategorised" (topicId/topicName both null) must still
            // re-report feedback — just with null topic fields, same convention as the topic re-report.
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "What's the weather?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "It's sunny today.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerMessageId.ToString() }));

            var messageRatings = new Dictionary<string, string> { [answerMessageId.ToString()] = "thumbsUp" };

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, messageRatings));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock("NONE");
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageFeedbackAsync(
                It.IsAny<DxpChatbotEventContext>(), answerMessageId.ToString(), true, null, null, FixedDate, FixedDate, TopicVersion), Times.Once);
        }

        // ==================== Reclassification must never touch resolution judgment (developer directive) ====================

        [Fact]
        public async Task ProcessConversationAsync_ExistingRowAlreadyResolved_PreservesIsResolvedAndAnswerMessageId()
        {
            // A topic taxonomy change has nothing to do with whether a question/answer exchange was
            // resolved — reclassifying must never reset or recompute a resolution already judged.
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();
            var oldDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

            _persistedClassifications.Add(new MessageTopicClassification
            {
                MessageId = messageId,
                SessionId = SessionId,
                AgentId = AgentId,
                TopicId = Guid.NewGuid(),
                TopicName = "Old Topic",
                MessageDate = oldDate,
                TopicVersion = "old-version",
                IsResolved = true,
                AnswerMessageId = answerMessageId
            });

            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, "Pay online via the portal.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerMessageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(ActiveTopicId, persisted.TopicId);
            Assert.Equal("Billing", persisted.TopicName);
            Assert.Equal(TopicVersion, persisted.TopicVersion);
            Assert.True(persisted.IsResolved);
            Assert.Equal(answerMessageId, persisted.AnswerMessageId);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), messageId.ToString(), ActiveTopicId, "Billing", TopicVersion, oldDate, FixedDate,
                true, "How do I pay my invoice?", "Pay online via the portal.", answerMessageId.ToString()), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_ExistingRowNeverJudged_StaysNull_NoResolutionFabricated()
        {
            var messageId = Guid.NewGuid();
            var oldDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

            _persistedClassifications.Add(new MessageTopicClassification
            {
                MessageId = messageId,
                SessionId = SessionId,
                AgentId = AgentId,
                TopicId = Guid.NewGuid(),
                TopicName = "Old Topic",
                MessageDate = oldDate,
                TopicVersion = "old-version",
                IsResolved = null,
                AnswerMessageId = null
            });

            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            var persisted = Assert.Single(_persistedClassifications);
            Assert.Null(persisted.IsResolved);
            Assert.Null(persisted.AnswerMessageId);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), messageId.ToString(), ActiveTopicId, "Billing", TopicVersion, oldDate, FixedDate), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_UsesRealMessageTimestampFromMetadata_NotJobRunTime()
        {
            var messageId = Guid.NewGuid();
            var realSentDate = new DateTimeOffset(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);

            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object>
                {
                    [CommonMetadataConsts.MessageId] = messageId.ToString(),
                    [CommonMetadataConsts.Timestamp] = realSentDate
                }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(realSentDate, persisted.MessageDate);

            _dxpChatbotAnalyticsServiceMock.Verify(x => x.ReportMessageTopicAsync(
                It.IsAny<DxpChatbotEventContext>(), messageId.ToString(), ActiveTopicId, "Billing", TopicVersion, realSentDate, FixedDate), Times.Once);
        }

        [Fact]
        public async Task ProcessConversationAsync_ExistingRowForMessageNoLongerInTranscript_IsRemoved()
        {
            var orphanMessageId = Guid.NewGuid();
            _persistedClassifications.Add(new MessageTopicClassification
            {
                MessageId = orphanMessageId,
                SessionId = SessionId,
                AgentId = AgentId,
                TopicId = Guid.NewGuid(),
                TopicName = "Stale Topic",
                MessageDate = FixedDate,
                TopicVersion = "old-version"
            });

            var messageId = Guid.NewGuid();
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "How do I pay my invoice?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() }));

            _chatStoreServiceMock
                .Setup(x => x.LoadChatAsync(TenantName, AgentId, SessionId, Guid.Empty.ToString()))
                .ReturnsAsync((FixedDate, "Session Name", chatHistory, (Dictionary<string, string>)null));

            var chatCompletionServiceMock = BuildChatCompletionServiceMock(ActiveTopicId.ToString());
            var dxpContext = BuildDxpContext();

            await _handler.ProcessConversationAsync(
                GetConversation(), TenantName, GetActiveTopics(), chatCompletionServiceMock.Object, dxpContext, TopicVersion, CancellationToken.None);

            Assert.DoesNotContain(_persistedClassifications, c => c.MessageId == orphanMessageId);
            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(messageId, persisted.MessageId);
        }
    }
}
