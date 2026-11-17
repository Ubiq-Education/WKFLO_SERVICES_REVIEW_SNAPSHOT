using AMAIS.WKFLO.AI.Application.Agents.Events.MessageResolutionAnalyzed;
using AMAIS.WKFLO.AI.Application.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Workflows
{
    /// <summary>
    /// Same real (temporary) SQLite database precedent as <see cref="MessageResolutionWorkflowActivityTests"/>
    /// — needed for the per-message <c>ExecuteUpdateAsync</c> calls, which MockQueryable/Moq can't translate.
    /// </summary>
    public class ConversationAnalysisWorkflowActivityTests : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly MasterDataDbContext _db;
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly ConversationAnalysisWorkflowActivity _activity;

        public ConversationAnalysisWorkflowActivityTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            _connection.Open();

            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(_connection).Options;
            _db = new MasterDataDbContext(options, Mock.Of<IDateTimeProvider>());
            _db.Database.EnsureCreated();

            _mediatorMock = new Mock<IWkfloMediator>();

            _activity = new ConversationAnalysisWorkflowActivity(
                _db,
                Mock.Of<IChatStoreService>(),
                Mock.Of<IChatNotificationService>(),
                new ChatContext(),
                NullLogger<ConversationAnalysisWorkflowActivity>.Instance,
                Mock.Of<IDateTimeProvider>(),
                _mediatorMock.Object);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        // ==================== ParseIsResolved (conversation-level) ====================
        // Most important regression coverage in this file: proves the EXISTING conversation-level
        // "isResolved" parsing is completely unaffected by a malformed/absent "messageResolutions" field.

        [Theory]
        [InlineData("{\"isResolved\": true}", true)]
        [InlineData("{\"isResolved\": false}", false)]
        [InlineData("{\"isResolved\": true, \"messageResolutions\": [{\"messageId\": \"11111111-1111-1111-1111-111111111111\", \"isResolved\": false}]}", true)]
        [InlineData("{\"isResolved\": false, \"messageResolutions\": \"not-an-array\"}", false)]
        [InlineData("{\"isResolved\": true, \"messageResolutions\": null}", true)]
        [InlineData("{\"isResolved\": false, \"messageResolutions\": [{\"messageId\": \"not-a-guid\"}]}", false)]
        [InlineData("{\"isResolved\": true, \"messageResolutions\": [1, 2, 3]}", true)]
        public void ParseIsResolved_UnaffectedByMessageResolutionsShapeOrAbsence(string content, bool expected)
        {
            Assert.Equal(expected, ConversationAnalysisWorkflowActivity.ParseIsResolved(content));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"messageResolutions\": [{\"messageId\": \"11111111-1111-1111-1111-111111111111\", \"isResolved\": true}]}")]
        public void ParseIsResolved_MalformedOrMissingIsResolved_DefaultsToTrue_RegardlessOfMessageResolutions(string content)
        {
            Assert.True(ConversationAnalysisWorkflowActivity.ParseIsResolved(content));
        }

        // ==================== ParseMessageResolutions ====================

        [Fact]
        public void ParseMessageResolutions_ValidArray_ReturnsAllEntries()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var content = $"{{\"isResolved\": true, \"messageResolutions\": [" +
                $"{{\"messageId\": \"{id1}\", \"isResolved\": true}}, " +
                $"{{\"messageId\": \"{id2}\", \"isResolved\": false}}]}}";

            var result = ConversationAnalysisWorkflowActivity.ParseMessageResolutions(content);

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, e => e.MessageId == id1 && e.IsResolved);
            Assert.Contains(result, e => e.MessageId == id2 && !e.IsResolved);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"isResolved\": true}")]
        [InlineData("{\"isResolved\": true, \"messageResolutions\": \"not-an-array\"}")]
        [InlineData("{\"isResolved\": true, \"messageResolutions\": null}")]
        public void ParseMessageResolutions_MissingOrMalformed_ReturnsNull(string content)
        {
            Assert.Null(ConversationAnalysisWorkflowActivity.ParseMessageResolutions(content));
        }

        [Fact]
        public void ParseMessageResolutions_SkipsEntriesWithInvalidGuidOrMissingFields_ButKeepsValidOnes()
        {
            var validId = Guid.NewGuid();
            var content = $"{{\"messageResolutions\": [" +
                $"{{\"messageId\": \"not-a-guid\", \"isResolved\": true}}, " +
                $"{{\"messageId\": \"{validId}\"}}, " +
                $"{{\"isResolved\": true}}, " +
                $"{{\"messageId\": \"{validId}\", \"isResolved\": false}}]}}";

            var result = ConversationAnalysisWorkflowActivity.ParseMessageResolutions(content);

            var entry = Assert.Single(result);
            Assert.Equal(validId, entry.MessageId);
            Assert.False(entry.IsResolved);
        }

        // ==================== BuildConversationText ====================

        /// <summary>US 7502: the "Stopped" divider is not something the assistant said, so it never reaches the analysis.</summary>
        [Fact]
        public void BuildConversationText_SkipsTheStoppedMarker_ButKeepsTheAnswerBeforeIt()
        {
            var history = new ChatHistory();
            history.AddUserMessage("Write me a long answer");
            history.AddAssistantMessage("Here is the first part");
            history.Add(StoppedTurnMarker.Build(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            var roundTripped = System.Text.Json.JsonSerializer.Deserialize<ChatHistory>(System.Text.Json.JsonSerializer.Serialize(history));

            var text = ConversationAnalysisWorkflowActivity.BuildConversationText(roundTripped);

            Assert.Contains("Assistant: Here is the first part", text);
            Assert.DoesNotContain(StoppedTurnMarker.Text, text);
        }

        [Fact]
        public void BuildConversationText_AnnotatesUserMessages_WithTheirOwnMessageId()
        {
            var messageId = Guid.NewGuid();
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "How do I reset my password?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() });
            history.AddAssistantMessage("Click 'Forgot password'.");

            var text = ConversationAnalysisWorkflowActivity.BuildConversationText(history);

            Assert.Contains($"[MessageId: {messageId}] User: How do I reset my password?", text);
            Assert.Contains("Assistant: Click 'Forgot password'.", text);
        }

        [Fact]
        public void BuildConversationText_UserMessageWithNoMessageIdMetadata_IncludedWithoutMarker()
        {
            var history = new ChatHistory();
            history.AddUserMessage("Hello?");

            var text = ConversationAnalysisWorkflowActivity.BuildConversationText(history);

            Assert.Equal("User: Hello?", text.Trim());
        }

        /// <summary>Bug 8189: an attached document is not conversation text, whether it is the short message or the old whole-text one.</summary>
        [Fact]
        public void BuildConversationText_SkipsAttachedDocuments_InBothFormats()
        {
            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.User,
                [new TextContent("Attached document: 'big.pdf' (application/pdf, 11.0 MB)", metadata: new Dictionary<string, object> { [CommonMetadataConsts.FileName] = "big.pdf" })]));
            history.Add(new ChatMessageContent(AuthorRole.User,
                [new TextContent("Document content:\nLEGACY-FULL-TEXT", metadata: new Dictionary<string, object> { [CommonMetadataConsts.FileName] = "old.pdf" })]));
            history.AddUserMessage("What is in the report?");
            history.AddAssistantMessage("Sales grew.");

            var text = ConversationAnalysisWorkflowActivity.BuildConversationText(history);

            Assert.DoesNotContain("big.pdf", text);
            Assert.DoesNotContain("LEGACY-FULL-TEXT", text);
            Assert.Contains("User: What is in the report?", text);
            Assert.Contains("Assistant: Sales grew.", text);
        }

        // ==================== ExtractQuestionAndAnswer ====================

        [Fact]
        public void ExtractQuestionAndAnswer_FindsAnswer_SkippingNonAssistantMessagesInBetween()
        {
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "What are your hours?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() });
            history.AddMessage(AuthorRole.Tool, "tool noise");
            history.AddMessage(AuthorRole.Assistant, "9 to 5, Monday to Friday.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerMessageId.ToString() });

            var (question, answer, resolvedAnswerMessageId) = ConversationAnalysisWorkflowActivity.ExtractQuestionAndAnswer(history, messageId);

            Assert.Equal("What are your hours?", question);
            Assert.Equal("9 to 5, Monday to Friday.", answer);
            Assert.Equal(answerMessageId, resolvedAnswerMessageId);
        }

        [Fact]
        public void ExtractQuestionAndAnswer_UnknownMessageId_ReturnsNulls()
        {
            var history = new ChatHistory();
            history.AddUserMessage("Hi");

            var (question, answer, answerMessageId) = ConversationAnalysisWorkflowActivity.ExtractQuestionAndAnswer(history, Guid.NewGuid());

            Assert.Null(question);
            Assert.Null(answer);
            Assert.Null(answerMessageId);
        }

        [Fact]
        public void ExtractQuestionAndAnswer_SkipsContactFormFiller_FindsTheRealAnswerAfterIt()
        {
            var messageId = Guid.NewGuid();
            var realAnswerMessageId = Guid.NewGuid();
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "What is the soccer team name?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() });
            history.AddMessage(AuthorRole.Assistant, "Please fill out the contact form to receive details.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.IsContactFormFiller] = true });
            history.AddMessage(AuthorRole.Assistant, "The soccer team is the Falcons.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = realAnswerMessageId.ToString() });

            var (question, answer, answerMessageId) = ConversationAnalysisWorkflowActivity.ExtractQuestionAndAnswer(history, messageId);

            Assert.Equal("What is the soccer team name?", question);
            Assert.Equal("The soccer team is the Falcons.", answer);
            Assert.Equal(realAnswerMessageId, answerMessageId);
        }

        [Fact]
        public void ExtractQuestionAndAnswer_OnlyContactFormFillerExists_ReturnsNullAnswer_NotTheFiller()
        {
            var messageId = Guid.NewGuid();
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "What is the soccer team name?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() });
            history.AddMessage(AuthorRole.Assistant, "Please fill out the contact form to receive details.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.IsContactFormFiller] = true });

            var (question, answer, answerMessageId) = ConversationAnalysisWorkflowActivity.ExtractQuestionAndAnswer(history, messageId);

            Assert.Equal("What is the soccer team name?", question);
            Assert.Null(answer);
            Assert.Null(answerMessageId);
        }

        // ==================== PersistMessageResolutionsAsync ====================

        private static Conversation BuildConversation(string sessionId = "session-1") => new()
        {
            SessionId = sessionId,
            AgentId = 1,
            TenantId = 1,
            UserId = "user-1",
            SubjectId = "subject-1",
            IsTestChat = false,
            ConversationDate = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)
        };

        private static RunnableAgent BuildAgent() => new TenantAgent { Id = 1, IsActive = true };

        private async Task<MessageTopicClassification> SeedClassificationAsync(Guid messageId, string sessionId = "session-1", string topicVersion = null)
        {
            var classification = new MessageTopicClassification
            {
                MessageId = messageId,
                SessionId = sessionId,
                AgentId = 1,
                TopicId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                TopicName = "Billing",
                MessageDate = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero),
                TopicVersion = topicVersion
            };

            _db.MessageTopicClassifications.Add(classification);
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
            _db.Entry(classification).State = EntityState.Detached;

            return classification;
        }

        private static ChatHistory BuildHistoryFor(Guid messageId, string question, string answer, Guid? answerMessageId = null)
        {
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, question,
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() });
            history.AddMessage(AuthorRole.Assistant, answer,
                metadata: answerMessageId.HasValue
                    ? new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerMessageId.Value.ToString() }
                    : null);
            return history;
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_NoMessageResolutionsInContent_DoesNothing_AndNeverThrows()
        {
            var conversation = BuildConversation();
            var history = new ChatHistory();

            await _activity.PersistMessageResolutionsAsync(
                "{\"isResolved\": true}", conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_ValidEntry_UpdatesRow_AndPublishesWithExtractedQuestionAndAnswer()
        {
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();
            await SeedClassificationAsync(messageId, topicVersion: "topic-version-9");
            var conversation = BuildConversation();
            var history = BuildHistoryFor(messageId, "Can I get a refund?", "Yes, within 30 days.", answerMessageId);

            var content = $"{{\"isResolved\": true, \"messageResolutions\": [{{\"messageId\": \"{messageId}\", \"isResolved\": false}}]}}";

            MessageResolutionAnalyzedDomainEvent published = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<MessageResolutionAnalyzedDomainEvent, CancellationToken>((evt, _) => published = evt)
                .Returns(Task.CompletedTask);

            await _activity.PersistMessageResolutionsAsync(content, conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            var persisted = await _db.MessageTopicClassifications.AsNoTracking()
                .SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
            Assert.False(persisted.IsResolved);
            // US 7142 (later re-scope): the backfill path also resolves and persists the answer's own MessageId
            // from the chat history, same as the near-immediate MessageResolutionWorkflow.
            Assert.Equal(answerMessageId, persisted.AnswerMessageId);

            Assert.NotNull(published);
            Assert.Equal("acme", published.TenantName);
            Assert.Equal("session-1", published.SessionId);
            Assert.Equal("subject-1", published.SubjectId);
            Assert.Equal(messageId.ToString(), published.MessageId);
            Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), published.TopicId);
            Assert.Equal("Billing", published.TopicName);
            Assert.False(published.IsResolved);
            Assert.Equal("Can I get a refund?", published.Question);
            Assert.Equal("Yes, within 30 days.", published.Response);
            // US 7142: TopicVersion is read off the already-loaded classification row and carried through.
            Assert.Equal("topic-version-9", published.TopicVersion);
            Assert.Equal(answerMessageId.ToString(), published.AnswerMessageId);
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_ClassificationHasNoTopicVersion_PublishesNullTopicVersion()
        {
            var messageId = Guid.NewGuid();
            await SeedClassificationAsync(messageId, topicVersion: null);
            var conversation = BuildConversation();
            var history = BuildHistoryFor(messageId, "Can I get a refund?", "Yes, within 30 days.");

            var content = $"{{\"isResolved\": true, \"messageResolutions\": [{{\"messageId\": \"{messageId}\", \"isResolved\": true}}]}}";

            MessageResolutionAnalyzedDomainEvent published = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<MessageResolutionAnalyzedDomainEvent, CancellationToken>((evt, _) => published = evt)
                .Returns(Task.CompletedTask);

            await _activity.PersistMessageResolutionsAsync(content, conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            Assert.NotNull(published);
            Assert.Null(published.TopicVersion);
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_AlreadyResolved_SkipsEntry_NeverReReports()
        {
            // The near-immediate MessageResolutionWorkflow (or a previous run of this same backfill)
            // already judged this message. This backfill run must not touch it again — the message stays
            // reported exactly once, matching the "one message per question" rule.
            var messageId = Guid.NewGuid();
            var classification = await SeedClassificationAsync(messageId);
            await _db.MessageTopicClassifications
                .Where(m => m.MessageId == messageId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsResolved, true), TestContext.Current.CancellationToken);

            var conversation = BuildConversation();
            var history = BuildHistoryFor(messageId, "Can I get a refund?", "Yes, within 30 days.");

            var content = $"{{\"messageResolutions\": [{{\"messageId\": \"{messageId}\", \"isResolved\": false}}]}}";

            await _activity.PersistMessageResolutionsAsync(content, conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            var persisted = await _db.MessageTopicClassifications.AsNoTracking()
                .SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
            // Still the ORIGINAL value (true) — the new entry's isResolved:false must be ignored.
            Assert.True(persisted.IsResolved);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_ReclassifiedMessage_FreshRowWithNullIsResolved_IsJudgedAndReportedAgain()
        {
            // Simulates TopicClassificationJobHandler's batch reclassification: it deletes and recreates the
            // MessageTopicClassification row (new TopicVersion, IsResolved reset to null) — so this backfill
            // must still be able to judge and report it again under the new version, not skip it forever.
            var messageId = Guid.NewGuid();
            await SeedClassificationAsync(messageId, topicVersion: "topic-version-2");
            var conversation = BuildConversation();
            var history = BuildHistoryFor(messageId, "Can I get a refund?", "Yes, within 30 days.");

            var content = $"{{\"messageResolutions\": [{{\"messageId\": \"{messageId}\", \"isResolved\": true}}]}}";

            await _activity.PersistMessageResolutionsAsync(content, conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            var persisted = await _db.MessageTopicClassifications.AsNoTracking()
                .SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
            Assert.True(persisted.IsResolved);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_OnlyContactFormFillerFound_SkipsEntry_NoUpdateNoPublish()
        {
            // US 7142 corner case: the LLM still returned a "messageResolutions" entry for this message
            // (it saw SOME assistant reply in the transcript — the filler), but the only reply persisted so
            // far is ContactForm filler, not a real answer. No resolution should ever be computed/reported
            // from filler text, and the row must be left untouched for a later re-analysis to pick up once
            // a real answer exists.
            var messageId = Guid.NewGuid();
            await SeedClassificationAsync(messageId);
            var conversation = BuildConversation();
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "What is the soccer team name?",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = messageId.ToString() });
            history.AddMessage(AuthorRole.Assistant, "Please fill out the contact form to receive details.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.IsContactFormFiller] = true });

            var content = $"{{\"messageResolutions\": [{{\"messageId\": \"{messageId}\", \"isResolved\": false}}]}}";

            await _activity.PersistMessageResolutionsAsync(content, conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            var persisted = await _db.MessageTopicClassifications.AsNoTracking()
                .SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
            Assert.Null(persisted.IsResolved);
            Assert.Null(persisted.AnswerMessageId);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_MessageIdNotFoundForSession_SkipsEntry_NoUpdateNoPublish()
        {
            var unknownMessageId = Guid.NewGuid();
            var conversation = BuildConversation();
            var history = new ChatHistory();

            var content = $"{{\"messageResolutions\": [{{\"messageId\": \"{unknownMessageId}\", \"isResolved\": true}}]}}";

            await _activity.PersistMessageResolutionsAsync(content, conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_MessageBelongsToDifferentSession_IsNotUpdated()
        {
            // A row exists for this MessageId, but under a DIFFERENT session than the one being
            // re-analyzed — must not be touched (guards against an hallucinated/cross-session MessageId).
            var messageId = Guid.NewGuid();
            await SeedClassificationAsync(messageId, sessionId: "other-session");
            var conversation = BuildConversation(sessionId: "session-1");
            var history = new ChatHistory();

            var content = $"{{\"messageResolutions\": [{{\"messageId\": \"{messageId}\", \"isResolved\": true}}]}}";

            await _activity.PersistMessageResolutionsAsync(content, conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            var persisted = await _db.MessageTopicClassifications.AsNoTracking()
                .SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
            Assert.Null(persisted.IsResolved);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PersistMessageResolutionsAsync_OneEntrysDxpPublishThrows_OtherEntriesStillProcessed()
        {
            var throwingMessageId = Guid.NewGuid();
            var okMessageId = Guid.NewGuid();
            await SeedClassificationAsync(throwingMessageId);
            await SeedClassificationAsync(okMessageId);

            var conversation = BuildConversation();
            var history = new ChatHistory();
            foreach (var m in BuildHistoryFor(throwingMessageId, "Q1", "A1")) history.Add(m);
            foreach (var m in BuildHistoryFor(okMessageId, "Q2", "A2")) history.Add(m);

            var content = $"{{\"messageResolutions\": [" +
                $"{{\"messageId\": \"{throwingMessageId}\", \"isResolved\": true}}, " +
                $"{{\"messageId\": \"{okMessageId}\", \"isResolved\": true}}]}}";

            _mediatorMock
                .Setup(x => x.Publish(It.Is<MessageResolutionAnalyzedDomainEvent>(e => e.MessageId == throwingMessageId.ToString()), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("DXP unreachable"));
            _mediatorMock
                .Setup(x => x.Publish(It.Is<MessageResolutionAnalyzedDomainEvent>(e => e.MessageId == okMessageId.ToString()), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Must not throw despite the first entry's DXP publish failing.
            await _activity.PersistMessageResolutionsAsync(content, conversation, BuildAgent(), "acme", history, TestContext.Current.CancellationToken);

            // Both rows are updated regardless of the DXP outcome for either.
            var throwingRow = await _db.MessageTopicClassifications.AsNoTracking()
                .SingleAsync(m => m.MessageId == throwingMessageId, TestContext.Current.CancellationToken);
            var okRow = await _db.MessageTopicClassifications.AsNoTracking()
                .SingleAsync(m => m.MessageId == okMessageId, TestContext.Current.CancellationToken);
            Assert.True(throwingRow.IsResolved);
            Assert.True(okRow.IsResolved);

            _mediatorMock.Verify(x => x.Publish(It.Is<MessageResolutionAnalyzedDomainEvent>(e => e.MessageId == okMessageId.ToString()), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
