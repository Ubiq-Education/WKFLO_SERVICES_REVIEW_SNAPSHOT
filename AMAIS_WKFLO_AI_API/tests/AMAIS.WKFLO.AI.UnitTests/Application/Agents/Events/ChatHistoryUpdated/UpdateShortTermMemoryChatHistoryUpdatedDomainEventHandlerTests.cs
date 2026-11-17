using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ConversationStarted;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Events.ChatHistoryUpdated
{
    /// <summary>
    /// Covers the fix for US 5911 Phase 2: resolution-analysis scheduling must no longer depend on
    /// <c>RunnableAgent.EnableSTM</c> — only the STM blob persistence itself (SaveChatAsync) is genuinely
    /// STM-specific. The Conversation row bookkeeping and analysis-workflow scheduling must both happen
    /// unconditionally.
    /// </summary>
    public class UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandlerTests
    {
        private const string TenantName = "test-tenant";
        private const string SessionId = "sess-1";
        private const string UserGuid = "user-guid-1";
        private const string SubjectId = "subject-guid-1";

        private readonly Mock<IChatStoreService> _chatStoreServiceMock;
        private readonly Mock<IConversationAnalysisWorkflowScheduler> _workflowSchedulerMock;
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly Mock<IConversationTitleWorkflowScheduler> _titleSchedulerMock = new();
        private readonly List<Conversation> _conversations = [];
        private readonly List<EntityTag> _entityTags = [];
        private readonly ChatContext _chatContext = new() { SubjectId = SubjectId };

        public UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandlerTests()
        {
            _chatStoreServiceMock = new Mock<IChatStoreService>();
            _chatStoreServiceMock
                .Setup(x => x.SaveChatAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ChatHistory>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()))
                .ReturnsAsync((3, 1));

            _workflowSchedulerMock = new Mock<IConversationAnalysisWorkflowScheduler>();
            _workflowSchedulerMock
                .Setup(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationAnalysisWorkflowInput>()))
                .Returns(Task.CompletedTask);

            _mediatorMock = new Mock<IWkfloMediator>();
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<ConversationStartedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler BuildHandler()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { new() { Id = 1, Name = TenantName } }.BuildMockDbSet().Object);

            var conversationsDbSet = _conversations.BuildMockDbSet();
            conversationsDbSet
                .Setup(x => x.AddAsync(It.IsAny<Conversation>(), It.IsAny<CancellationToken>()))
                .Callback<Conversation, CancellationToken>((c, _) => _conversations.Add(c));
            db.Setup(x => x.Conversations).Returns(conversationsDbSet.Object);
            db.Setup(x => x.EntityTags).Returns(_entityTags.BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            dateTimeProviderMock.Setup(x => x.DateTimeUtcNow()).Returns(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

            return new UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler(
                _chatStoreServiceMock.Object,
                new TenantContext { TenantName = TenantName },
                new UserContext { Guid = UserGuid },
                _chatContext,
                db.Object,
                _workflowSchedulerMock.Object,
                dateTimeProviderMock.Object,
                _mediatorMock.Object,
                _titleSchedulerMock.Object);
        }

        private static readonly DateTimeOffset FixedCreatedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private static ChatHistoryUpdatedDomainEvent BuildNotification(RunnableAgent agent, DateTimeOffset? createdAt = null)
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("hello");

            return new ChatHistoryUpdatedDomainEvent(agent, SessionId, null, createdAt ?? FixedCreatedAt, DateTimeOffset.UtcNow, chatHistory, Mock.Of<IChatCompletionService>());
        }

        [Fact]
        public async Task Handle_EnableSTMFalse_StillSchedulesAnalysisWorkflow()
        {
            // The exact coverage gap this fix closes: ConversationAnalysisWorkflowActivity loads chat history
            // straight from blob storage, nothing STM-specific — so scheduling must not depend on EnableSTM.
            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            _workflowSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.Is<ConversationAnalysisWorkflowInput>(
                i => i.TenantName == TenantName && i.AgentId == agent.Id && i.SessionId == SessionId)), Times.Once);
        }

        [Fact]
        public async Task Handle_EnableSTMFalse_DoesNotPersistToStmBlobStorage()
        {
            // SaveChatAsync is the genuinely STM-specific piece — it must stay gated behind EnableSTM.
            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            _chatStoreServiceMock.Verify(x => x.SaveChatAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ChatHistory>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_EnableSTMFalse_StillCreatesConversationRow()
        {
            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            var created = Assert.Single(_conversations);
            Assert.Equal(SessionId, created.SessionId);
            Assert.Equal(agent.Id, created.AgentId);
            // No STM save happened, so there are no fresh rating counts to report — default to 0, not crash.
            Assert.Equal(0, created.TotalThumbsUp);
            Assert.Equal(0, created.TotalThumbsDown);
        }

        [Fact]
        public async Task Handle_EnableSTMTrue_PersistsToStmBlobAndSchedulesAnalysisWorkflow()
        {
            var agent = new TenantAgent { Id = 5, EnableSTM = true };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            _chatStoreServiceMock.Verify(x => x.SaveChatAsync(
                TenantName, agent.Id, SessionId, It.IsAny<string>(), It.IsAny<ChatHistory>(), It.IsAny<DateTimeOffset>(), UserGuid),
                Times.Once);

            _workflowSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationAnalysisWorkflowInput>()), Times.Once);

            var created = Assert.Single(_conversations);
            Assert.Equal(3, created.TotalThumbsUp);
            Assert.Equal(1, created.TotalThumbsDown);
        }

        [Fact]
        public async Task Handle_EnableSTMFalse_ExistingConversationWithRatings_DoesNotZeroThemOut()
        {
            // Regression guard: before this fix, this handler never ran at all for STM-disabled agents, so it
            // never had a chance to clobber TotalThumbsUp/Down. Now that it runs unconditionally, it must not
            // start zeroing out rating counts that some other path (e.g. StoreConversationTenantAgentById) set.
            _conversations.Add(new Conversation
            {
                Id = 1,
                AgentId = 5,
                SessionId = SessionId,
                TotalThumbsUp = 4,
                TotalThumbsDown = 2
            });

            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            var existing = Assert.Single(_conversations);
            Assert.Equal(4, existing.TotalThumbsUp);
            Assert.Equal(2, existing.TotalThumbsDown);
        }

        [Fact]
        public async Task Handle_NoMatchingTenant_DoesNotScheduleAnalysisWorkflow()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(new List<Tenant>().BuildMockDbSet().Object);
            db.Setup(x => x.Conversations).Returns(_conversations.BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(DateTimeOffset.UtcNow);
            dateTimeProviderMock.Setup(x => x.DateTimeUtcNow()).Returns(DateTime.UtcNow);

            var handler = new UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler(
                _chatStoreServiceMock.Object,
                new TenantContext { TenantName = TenantName },
                new UserContext { Guid = UserGuid },
                new ChatContext { SubjectId = SubjectId },
                db.Object,
                _workflowSchedulerMock.Object,
                dateTimeProviderMock.Object,
                _mediatorMock.Object,
                _titleSchedulerMock.Object);

            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            _workflowSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationAnalysisWorkflowInput>()), Times.Never);
        }

        [Fact]
        public async Task Handle_NewConversation_PublishesConversationStartedDomainEvent()
        {
            // The new, order-independent signal ReportConversationStartedDomainEventHandler now relies on
            // entirely: publishing this event IS the "new conversation" signal, by construction.
            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(
                It.Is<ConversationStartedDomainEvent>(e => e.SessionId == SessionId && e.RunnableAgent.Id == agent.Id),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_NewConversation_PublishesConversationStartedDomainEventWithConversationDate()
        {
            // ConversationDate must be threaded from the same CreatedAt used for Conversation.ConversationDate,
            // not left null or re-derived independently.
            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent, FixedCreatedAt), TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(
                It.Is<ConversationStartedDomainEvent>(e => e.ConversationDate == FixedCreatedAt),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ExistingConversation_DoesNotPublishConversationStartedDomainEvent()
        {
            _conversations.Add(new Conversation
            {
                Id = 1,
                AgentId = 5,
                SessionId = SessionId
            });

            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(It.IsAny<ConversationStartedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_NewConversation_PersistsSubjectIdFromChatContext()
        {
            // Regression guard for US 5911: this is the real per-message-save path, and DXP event reporting
            // for the background resolution-analysis workflow depends on Conversation.SubjectId being set here
            // (the workflow has no live HTTP request to read ChatContext from directly).
            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            var created = Assert.Single(_conversations);
            Assert.Equal(SubjectId, created.SubjectId);
        }

        [Fact]
        public async Task Handle_ExistingConversation_UpdatesSubjectIdFromChatContext()
        {
            _conversations.Add(new Conversation
            {
                Id = 1,
                AgentId = 5,
                SessionId = SessionId,
                SubjectId = null
            });

            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent), TestContext.Current.CancellationToken);

            var existing = Assert.Single(_conversations);
            Assert.Equal(SubjectId, existing.SubjectId);
        }

        /// <summary>
        /// US 7502 (user decision): a stopped turn's save (the pre-turn history plus the "Stopped" marker) adds no content.
        /// It is persisted and the conversation is synced, but no analysis is scheduled and the analysis status is left as
        /// it was, whatever it was (so a conversation is never left waiting in Pending for an analysis that never comes).
        /// </summary>
        [Theory]
        [InlineData(ConversationAnalysisStatus.Completed)]
        [InlineData(ConversationAnalysisStatus.Pending)]
        [InlineData(ConversationAnalysisStatus.Processing)]
        public async Task Handle_StoppedTurn_SavesButIsNotAnalysed_AndLeavesTheStatus(ConversationAnalysisStatus status)
        {
            _conversations.Add(new Conversation { Id = 1, AgentId = 5, SessionId = SessionId, AnalysisStatus = status });
            var handler = BuildHandler();
            var notification = BuildNotification(new TenantAgent { Id = 5, EnableSTM = true });
            notification.ChatHistory.Add(StoppedTurnMarker.Build(Guid.NewGuid().ToString(), FixedCreatedAt, FixedCreatedAt));
            notification.IsStoppedTurn = true;

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _chatStoreServiceMock.Verify(x => x.SaveChatAsync(
                TenantName, 5, SessionId, It.IsAny<string>(), notification.ChatHistory, It.IsAny<DateTimeOffset>(), UserGuid),
                Times.Once);
            Assert.Equal(status, Assert.Single(_conversations).AnalysisStatus);
            _workflowSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationAnalysisWorkflowInput>()), Times.Never);
        }

        [Fact]
        public async Task Handle_NormalTurnAfterAStop_SetsPendingAndSchedulesTheAnalysis()
        {
            _conversations.Add(new Conversation { Id = 1, AgentId = 5, SessionId = SessionId, AnalysisStatus = ConversationAnalysisStatus.Completed });
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = true }), TestContext.Current.CancellationToken);

            Assert.Equal(ConversationAnalysisStatus.Pending, Assert.Single(_conversations).AnalysisStatus);
            _workflowSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationAnalysisWorkflowInput>()), Times.Once);
        }

        /// <summary>
        /// US 7507: a new Conversation row gets the first user message as its PreviewLine, whether or not STM is on.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Handle_NewConversation_SetsPreviewLineToTheFirstUserMessage(bool enableStm)
        {
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = enableStm }), TestContext.Current.CancellationToken);

            Assert.Equal("hello", Assert.Single(_conversations).PreviewLine);
        }

        [Fact]
        public async Task Handle_NewConversation_TruncatesPreviewLineTo300Characters()
        {
            var handler = BuildHandler();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(new string('a', 350));
            var notification = new ChatHistoryUpdatedDomainEvent(new TenantAgent { Id = 5, EnableSTM = true }, SessionId, null, FixedCreatedAt, FixedCreatedAt, chatHistory, Mock.Of<IChatCompletionService>());

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            Assert.Equal(300, Assert.Single(_conversations).PreviewLine.Length);
        }

        /// <summary>
        /// US 7507: the preview is set once. Later turns, and a later AI title in the chat name, never replace it.
        /// </summary>
        [Fact]
        public async Task Handle_ExistingConversationWithPreviewLine_KeepsIt()
        {
            _conversations.Add(new Conversation { Id = 1, AgentId = 5, SessionId = SessionId, Name = "AI title", PreviewLine = "original first message" });
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = true }), TestContext.Current.CancellationToken);

            Assert.Equal("original first message", Assert.Single(_conversations).PreviewLine);
        }

        [Fact]
        public async Task Handle_ExistingConversationWithoutPreviewLine_FillsIt()
        {
            _conversations.Add(new Conversation { Id = 1, AgentId = 5, SessionId = SessionId });
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = false }), TestContext.Current.CancellationToken);

            Assert.Equal("hello", Assert.Single(_conversations).PreviewLine);
        }

        /// <summary>
        /// A turn adds each image/document attachment as its own user entry BEFORE the typed text. The preview line
        /// and the first-turn chat name must both be the typed text, not the attachment's content.
        /// </summary>
        [Fact]
        public async Task Handle_FirstQuestionWithAttachments_UsesTheTypedTextForPreviewLineAndChatName()
        {
            var handler = BuildHandler();
            var notification = new ChatHistoryUpdatedDomainEvent(new TenantAgent { Id = 5, EnableSTM = true }, SessionId, null, FixedCreatedAt, FixedCreatedAt,
                BuildHistoryWithAttachments("Summarise this report"), Mock.Of<IChatCompletionService>());

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            var conversation = Assert.Single(_conversations);
            Assert.Equal("Summarise this report", conversation.PreviewLine);
            Assert.Equal("Summarise this report", conversation.Name);
            _chatStoreServiceMock.Verify(x => x.SaveChatAsync(TenantName, 5, SessionId, "Summarise this report", It.IsAny<ChatHistory>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Handle_FirstQuestionWithOnlyAttachments_LeavesPreviewLineEmpty()
        {
            var handler = BuildHandler();
            var notification = new ChatHistoryUpdatedDomainEvent(new TenantAgent { Id = 5, EnableSTM = false }, SessionId, null, FixedCreatedAt, FixedCreatedAt,
                BuildHistoryWithAttachments(string.Empty), Mock.Of<IChatCompletionService>());

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            Assert.Null(Assert.Single(_conversations).PreviewLine);
        }

        /// <summary>
        /// Only the first question counts: a later typed message never becomes the preview.
        /// </summary>
        [Fact]
        public async Task Handle_LaterQuestions_DoNotChangeWhichMessageIsThePreview()
        {
            var handler = BuildHandler();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("first question");
            chatHistory.AddAssistantMessage("an answer");
            chatHistory.AddUserMessage("second question");
            var notification = new ChatHistoryUpdatedDomainEvent(new TenantAgent { Id = 5, EnableSTM = false }, SessionId, null, FixedCreatedAt, FixedCreatedAt,
                chatHistory, Mock.Of<IChatCompletionService>());

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            Assert.Equal("first question", Assert.Single(_conversations).PreviewLine);
        }

        // Same shape SemanticKernelAgentServiceRun builds: image entry, document entry, then the typed text.
        private static ChatHistory BuildHistoryWithAttachments(string typedText)
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddMessage(AuthorRole.User,
                new ChatMessageContentItemCollection
                {
                    new TextContent("\n FileName: 'photo.png'\n FileType: 'image/png'"),
                    new ImageContent(new byte[] { 1, 2, 3 }, "image/png")
                },
                metadata: new Dictionary<string, object> { { CommonMetadataConsts.FileName, "photo.png" } });
            chatHistory.AddMessage(AuthorRole.User,
                new ChatMessageContentItemCollection
                {
                    new TextContent("Document content:\nQuarterly numbers", metadata: new Dictionary<string, object> { { CommonMetadataConsts.FileName, "report.pdf" } })
                });
            chatHistory.AddMessage(AuthorRole.User, new ChatMessageContentItemCollection { new TextContent(typedText) });
            return chatHistory;
        }

        private void TagAsMaisy(int agentId) =>
            _entityTags.Add(new EntityTag { TagId = 1, EntityType = TaggableEntityTypes.Agent, EntityId = agentId, Tag = new Tag { Id = 1, Name = FeatureTagConsts.Maisy } });

        private static ChatHistoryUpdatedDomainEvent BuildExchangeNotification(RunnableAgent agent, string chatName = null)
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("Can you help me plan the trip?");
            chatHistory.AddAssistantMessage("Of course. Where are you going?");

            return new ChatHistoryUpdatedDomainEvent(agent, SessionId, chatName, FixedCreatedAt, FixedCreatedAt, chatHistory, Mock.Of<IChatCompletionService>());
        }

        /// <summary>
        /// US 7507: a MAISY conversation's first completed exchange schedules its AI title, with the exchange as input.
        /// </summary>
        [Fact]
        public async Task Handle_MaisyFirstExchange_SchedulesTitleGeneration()
        {
            TagAsMaisy(5);
            var handler = BuildHandler();

            await handler.Handle(BuildExchangeNotification(new TenantAgent { Id = 5, EnableSTM = true }), TestContext.Current.CancellationToken);

            _titleSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.Is<ConversationTitleWorkflowInput>(i =>
                i.TenantName == TenantName && i.AgentId == 5 && i.SessionId == SessionId
                && i.FirstUserMessage == "Can you help me plan the trip?"
                && i.FirstAssistantMessage == "Of course. Where are you going?")), Times.Once);
            Assert.Equal(ConversationTitleStatus.Pending, Assert.Single(_conversations).TitleStatus);
        }

        /// <summary>
        /// The title is generated from the typed question, not from an attachment added before it.
        /// </summary>
        [Fact]
        public async Task Handle_MaisyFirstExchangeWithAttachments_TitlesFromTheTypedText()
        {
            TagAsMaisy(5);
            var handler = BuildHandler();
            var chatHistory = BuildHistoryWithAttachments("Summarise this report");
            chatHistory.AddAssistantMessage("Here is the summary.");
            var notification = new ChatHistoryUpdatedDomainEvent(new TenantAgent { Id = 5, EnableSTM = true }, SessionId, null, FixedCreatedAt, FixedCreatedAt,
                chatHistory, Mock.Of<IChatCompletionService>());

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            _titleSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.Is<ConversationTitleWorkflowInput>(i =>
                i.FirstUserMessage == "Summarise this report" && i.FirstAssistantMessage == "Here is the summary.")), Times.Once);
        }

        [Fact]
        public async Task Handle_MaisyWithoutAnAnswerYet_DoesNotScheduleTitleGeneration()
        {
            TagAsMaisy(5);
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = true }), TestContext.Current.CancellationToken);

            _titleSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationTitleWorkflowInput>()), Times.Never);
            Assert.Equal(ConversationTitleStatus.Pending, Assert.Single(_conversations).TitleStatus);
        }

        [Fact]
        public async Task Handle_NotMaisy_MarksTitleSkipped_AndDoesNotSchedule()
        {
            var handler = BuildHandler();

            await handler.Handle(BuildExchangeNotification(new TenantAgent { Id = 5, EnableSTM = true }), TestContext.Current.CancellationToken);

            _titleSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationTitleWorkflowInput>()), Times.Never);
            Assert.Equal(ConversationTitleStatus.Skipped, Assert.Single(_conversations).TitleStatus);
        }

        [Fact]
        public async Task Handle_MaisyTestChat_MarksTitleSkipped()
        {
            TagAsMaisy(5);
            _chatContext.IsTestChat = true;
            var handler = BuildHandler();

            await handler.Handle(BuildExchangeNotification(new TenantAgent { Id = 5, EnableSTM = true }), TestContext.Current.CancellationToken);

            _titleSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationTitleWorkflowInput>()), Times.Never);
            Assert.Equal(ConversationTitleStatus.Skipped, Assert.Single(_conversations).TitleStatus);
        }

        [Theory]
        [InlineData(ConversationTitleStatus.Processing)]
        [InlineData(ConversationTitleStatus.Generated)]
        [InlineData(ConversationTitleStatus.Failed)]
        public async Task Handle_TitleAlreadyHandled_DoesNotScheduleAgain(ConversationTitleStatus status)
        {
            TagAsMaisy(5);
            _conversations.Add(new Conversation { Id = 1, AgentId = 5, SessionId = SessionId, Name = "Trip planning", TitleStatus = status });
            var handler = BuildHandler();

            await handler.Handle(BuildExchangeNotification(new TenantAgent { Id = 5, EnableSTM = true }, chatName: "Can you help me plan the trip?"), TestContext.Current.CancellationToken);

            _titleSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationTitleWorkflowInput>()), Times.Never);
            Assert.Equal(status, Assert.Single(_conversations).TitleStatus);
        }

        [Fact]
        public async Task Handle_TitleSchedulingFails_DoesNotBreakTheTurn()
        {
            TagAsMaisy(5);
            _titleSchedulerMock
                .Setup(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationTitleWorkflowInput>()))
                .ThrowsAsync(new InvalidOperationException("Dapr is down"));
            var handler = BuildHandler();

            await handler.Handle(BuildExchangeNotification(new TenantAgent { Id = 5, EnableSTM = true }), TestContext.Current.CancellationToken);

            Assert.Equal(ConversationTitleStatus.Pending, Assert.Single(_conversations).TitleStatus);
        }

        /// <summary>
        /// US 7507: the chat name a turn starts with is read from the blob, so it can predate the AI title. A Generated
        /// title wins: it is kept on the row and written back onto the blob.
        /// </summary>
        [Fact]
        public async Task Handle_GeneratedTitle_IsKeptOnTheRow_AndWrittenToTheBlob()
        {
            _conversations.Add(new Conversation { Id = 1, AgentId = 5, SessionId = SessionId, Name = "Trip planning", TitleStatus = ConversationTitleStatus.Generated });
            var handler = BuildHandler();

            await handler.Handle(BuildExchangeNotification(new TenantAgent { Id = 5, EnableSTM = true }, chatName: "Can you help me plan the trip?"), TestContext.Current.CancellationToken);

            Assert.Equal("Trip planning", Assert.Single(_conversations).Name);
            _chatStoreServiceMock.Verify(x => x.SaveChatAsync(
                TenantName, 5, SessionId, "Trip planning", It.IsAny<ChatHistory>(), It.IsAny<DateTimeOffset>(), UserGuid), Times.Once);
        }

        [Fact]
        public async Task Handle_TitleNotGenerated_ChatNameStillUpdatesTheRow()
        {
            _conversations.Add(new Conversation { Id = 1, AgentId = 5, SessionId = SessionId, Name = "old", TitleStatus = ConversationTitleStatus.Failed });
            var handler = BuildHandler();

            await handler.Handle(BuildExchangeNotification(new TenantAgent { Id = 5, EnableSTM = true }, chatName: "from blob"), TestContext.Current.CancellationToken);

            Assert.Equal("from blob", Assert.Single(_conversations).Name);
        }

        private static Conversation BuildDeletedConversationWithOldState(bool isTestChat) => new()
        {
            Id = 1,
            AgentId = 5,
            TenantId = 1,
            SessionId = SessionId,
            IsDeleted = true,
            Name = "Old conversation",
            PreviewLine = "Old preview",
            TitleStatus = ConversationTitleStatus.Generated,
            ConversationDate = new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero),
            UserId = "old-user-guid",
            SubjectId = "old-subject-guid",
            TotalThumbsUp = 7,
            TotalThumbsDown = 5,
            LastActivityAt = new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero),
            IsResolved = false,
            AnalysisStatus = ConversationAnalysisStatus.Completed,
            IsTestChat = isTestChat
        };

        /// <summary>
        /// Bug 7958: a soft-deleted conversation still matches (AgentId, SessionId) when the client keeps chatting on the
        /// same session id. It must be revived in place (no duplicate row) as a brand-new conversation, with none of the
        /// deleted conversation's per-conversation state surviving.
        /// </summary>
        [Fact]
        public async Task Handle_DeletedConversation_RevivesItInPlaceAsANewConversation()
        {
            _conversations.Add(BuildDeletedConversationWithOldState(isTestChat: false));
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = true }, FixedCreatedAt), TestContext.Current.CancellationToken);

            var revived = Assert.Single(_conversations);
            Assert.Equal(1, revived.Id);
            Assert.False(revived.IsDeleted);
            Assert.Equal("hello", revived.Name);
            Assert.NotEqual("Old preview", revived.PreviewLine);
            Assert.NotEqual(ConversationTitleStatus.Generated, revived.TitleStatus);
            Assert.Equal(FixedCreatedAt, revived.ConversationDate);
            Assert.Equal(UserGuid, revived.UserId);
            Assert.Equal(SubjectId, revived.SubjectId);
            Assert.Equal(3, revived.TotalThumbsUp);
            Assert.Equal(1, revived.TotalThumbsDown);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), revived.LastActivityAt);
            Assert.True(revived.IsResolved);
            Assert.Equal(ConversationAnalysisStatus.Pending, revived.AnalysisStatus);
            Assert.False(revived.IsTestChat);
            _workflowSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationAnalysisWorkflowInput>()), Times.Once);
        }

        [Fact]
        public async Task Handle_DeletedConversation_StoppedTurn_RevivesButLeavesTheStatus()
        {
            // US 7502: a stopped turn schedules no analysis, so the revived row must not be left waiting in Pending.
            _conversations.Add(BuildDeletedConversationWithOldState(isTestChat: false));
            var handler = BuildHandler();
            var notification = BuildNotification(new TenantAgent { Id = 5, EnableSTM = true }, FixedCreatedAt);
            notification.ChatHistory.Add(StoppedTurnMarker.Build(Guid.NewGuid().ToString(), FixedCreatedAt, FixedCreatedAt));
            notification.IsStoppedTurn = true;

            await handler.Handle(notification, TestContext.Current.CancellationToken);

            var revived = Assert.Single(_conversations);
            Assert.False(revived.IsDeleted);
            Assert.Equal(ConversationAnalysisStatus.Completed, revived.AnalysisStatus);
            _workflowSchedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<ConversationAnalysisWorkflowInput>()), Times.Never);
        }

        [Fact]
        public async Task Handle_DeletedConversation_StmDisabled_ResetsRatingsToZero()
        {
            // With STM disabled there is no fresh rating count; the deleted conversation's counts must not leak into the new one.
            _conversations.Add(BuildDeletedConversationWithOldState(isTestChat: false));
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = false }), TestContext.Current.CancellationToken);

            var revived = Assert.Single(_conversations);
            Assert.Equal(0, revived.TotalThumbsUp);
            Assert.Equal(0, revived.TotalThumbsDown);
        }

        [Fact]
        public async Task Handle_DeletedConversation_PublishesConversationStartedDomainEvent()
        {
            _conversations.Add(BuildDeletedConversationWithOldState(isTestChat: false));
            var agent = new TenantAgent { Id = 5, EnableSTM = false };
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(agent, FixedCreatedAt), TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(
                It.Is<ConversationStartedDomainEvent>(e => e.SessionId == SessionId && e.RunnableAgent.Id == agent.Id && e.ConversationDate == FixedCreatedAt),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task Handle_DeletedConversation_IsTestChatTakesTheCurrentChatContextValue(bool wasTestChat, bool isTestChat)
        {
            _conversations.Add(BuildDeletedConversationWithOldState(wasTestChat));
            _chatContext.IsTestChat = isTestChat;
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = false }), TestContext.Current.CancellationToken);

            Assert.Equal(isTestChat, Assert.Single(_conversations).IsTestChat);
        }

        [Fact]
        public async Task Handle_ExistingNotDeletedConversation_KeepsItsConversationStateAndIsTestChat()
        {
            // Regression guard for Bug 7958: the revival path must only apply to deleted rows.
            var conversationDate = new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero);
            _conversations.Add(new Conversation
            {
                Id = 1,
                AgentId = 5,
                SessionId = SessionId,
                ConversationDate = conversationDate,
                IsResolved = false,
                IsTestChat = true,
                TotalThumbsUp = 4,
                TotalThumbsDown = 2
            });
            var handler = BuildHandler();

            await handler.Handle(BuildNotification(new TenantAgent { Id = 5, EnableSTM = false }), TestContext.Current.CancellationToken);

            var existing = Assert.Single(_conversations);
            Assert.False(existing.IsDeleted);
            Assert.Equal(conversationDate, existing.ConversationDate);
            Assert.False(existing.IsResolved);
            Assert.True(existing.IsTestChat);
            Assert.Equal(4, existing.TotalThumbsUp);
            Assert.Equal(2, existing.TotalThumbsDown);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<ConversationStartedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
