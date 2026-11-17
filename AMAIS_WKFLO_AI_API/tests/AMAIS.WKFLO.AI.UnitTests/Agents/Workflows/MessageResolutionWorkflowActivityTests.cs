using AMAIS.WKFLO.AI.Application.Agents.Events.MessageResolutionAnalyzed;
using AMAIS.WKFLO.AI.Application.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Workflows
{
    /// <summary>
    /// Real (if temporary) SQLite database, same precedent as McpOAuthConsentServiceMultiReplicaTests in the
    /// MD API unit tests — needed because <c>ExecuteUpdateAsync</c> requires a genuine SQL translation, which
    /// MockQueryable/Moq-based <c>DbSet</c> mocks don't support.
    /// </summary>
    public class MessageResolutionWorkflowActivityTests : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly MasterDataDbContext _db;
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly MessageResolutionWorkflowActivity _activity;

        public MessageResolutionWorkflowActivityTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            _connection.Open();

            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(_connection).Options;
            _db = new MasterDataDbContext(options, Mock.Of<IDateTimeProvider>());
            _db.Database.EnsureCreated();

            _mediatorMock = new Mock<IWkfloMediator>();

            _activity = new MessageResolutionWorkflowActivity(
                _db,
                Mock.Of<IChatNotificationService>(),
                new ChatContext(),
                NullLogger<MessageResolutionWorkflowActivity>.Instance,
                Mock.Of<IDateTimeProvider>(),
                _mediatorMock.Object);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static MessageResolutionWorkflowInput BuildInput(Guid messageId, Guid? answerMessageId = null) => new()
        {
            TenantName = "acme",
            AgentId = 1,
            SessionId = "session-1",
            UserId = "user-1",
            MessageId = messageId,
            Question = "How do I reset my password?",
            Answer = "Click 'Forgot password' on the login screen.",
            AnswerMessageId = answerMessageId ?? Guid.NewGuid()
        };

        private async Task<MessageTopicClassification> SeedClassificationAsync(Guid messageId, Guid? topicId = null, string topicVersion = null)
        {
            var classification = new MessageTopicClassification
            {
                MessageId = messageId,
                SessionId = "session-1",
                AgentId = 1,
                TopicId = topicId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TopicName = "Account",
                MessageDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
                TopicVersion = topicVersion
            };

            _db.MessageTopicClassifications.Add(classification);
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Detach so the later ExecuteUpdateAsync-based UPDATE isn't shadowed by a locally tracked,
            // now-stale copy of the same row.
            _db.Entry(classification).State = EntityState.Detached;

            return classification;
        }

        private static RunnableAgent BuildAgent() => new TenantAgent { Id = 1, IsActive = true };

        // ---------- ParseIsResolved ----------

        [Theory]
        [InlineData("{\"isResolved\": true}", true)]
        [InlineData("{\"isResolved\": false}", false)]
        [InlineData("  {\"isResolved\": true}  ", true)]
        public void ParseIsResolved_ValidJson_ReturnsParsedValue(string content, bool expected)
        {
            Assert.Equal(expected, MessageResolutionWorkflowActivity.ParseIsResolved(content));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("{\"somethingElse\": true}")]
        [InlineData("{\"isResolved\": \"not-a-bool\"}")]
        public void ParseIsResolved_MalformedOrMissing_DefaultsToTrue(string content)
        {
            Assert.True(MessageResolutionWorkflowActivity.ParseIsResolved(content));
        }

        // ---------- PersistJudgmentAndReportAsync ----------

        [Fact]
        public async Task PersistJudgmentAndReportAsync_UpdatesMessageTopicClassification_ByMessageId()
        {
            var messageId = Guid.NewGuid();
            var answerMessageId = Guid.NewGuid();
            var classification = await SeedClassificationAsync(messageId);
            var input = BuildInput(messageId, answerMessageId);

            var updated = await _activity.PersistJudgmentAndReportAsync(classification, BuildAgent(), input, isResolved: true);

            Assert.True(updated);

            var persisted = await _db.MessageTopicClassifications.AsNoTracking().SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
            Assert.True(persisted.IsResolved);
            // US 7142: AnswerMessageId lets a later rating (given on the answer, not the question) look this
            // row up directly.
            Assert.Equal(answerMessageId, persisted.AnswerMessageId);
        }

        [Fact]
        public async Task PersistJudgmentAndReportAsync_RowNoLongerExists_ReturnsFalse_AndNeverReportsToDxp()
        {
            var missingMessageId = Guid.NewGuid();
            // A detached in-memory classification object standing in for "the row we loaded moments ago,
            // now gone" — nothing with this MessageId is actually seeded in the database.
            var classification = new MessageTopicClassification { MessageId = missingMessageId, SessionId = "session-1", AgentId = 1 };
            var input = BuildInput(missingMessageId);

            var updated = await _activity.PersistJudgmentAndReportAsync(classification, BuildAgent(), input, isResolved: true);

            Assert.False(updated);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PersistJudgmentAndReportAsync_OnSuccess_PublishesDxpEventWithCorrectArguments()
        {
            var messageId = Guid.NewGuid();
            var classification = await SeedClassificationAsync(messageId, topicId: Guid.Parse("22222222-2222-2222-2222-222222222222"), topicVersion: "topic-version-7");

            _db.Conversations.Add(new Conversation
            {
                SessionId = "session-1",
                AgentId = 1,
                TenantId = 1,
                UserId = "user-1",
                SubjectId = "subject-42",
                IsTestChat = false,
                ConversationDate = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero)
            });
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var answerMessageId = Guid.NewGuid();
            var input = BuildInput(messageId, answerMessageId);

            MessageResolutionAnalyzedDomainEvent published = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<MessageResolutionAnalyzedDomainEvent, CancellationToken>((evt, _) => published = evt)
                .Returns(Task.CompletedTask);

            await _activity.PersistJudgmentAndReportAsync(classification, BuildAgent(), input, isResolved: false);

            Assert.NotNull(published);
            Assert.Equal("acme", published.TenantName);
            Assert.Equal("session-1", published.SessionId);
            Assert.Equal("subject-42", published.SubjectId);
            Assert.Equal(1, published.TenantAgentId);
            Assert.Equal(messageId.ToString(), published.MessageId);
            Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), published.TopicId);
            Assert.Equal("Account", published.TopicName);
            Assert.False(published.IsResolved);
            Assert.Equal("How do I reset my password?", published.Question);
            Assert.Equal("Click 'Forgot password' on the login screen.", published.Response);
            // US 7142: TopicVersion is read off the already-loaded classification row and carried through.
            Assert.Equal("topic-version-7", published.TopicVersion);
            // US 7142 (later re-scope): the answer's own MessageId - distinct from the question's MessageId
            // this whole event is keyed by - is carried through so a later rating can look the row up directly.
            Assert.Equal(answerMessageId.ToString(), published.AnswerMessageId);
        }

        [Fact]
        public async Task PersistJudgmentAndReportAsync_ClassificationHasNoTopicVersion_PublishesNullTopicVersion()
        {
            // Rows persisted before this column existed (or by a path that never resolved a version) legitimately
            // have a null TopicVersion — must flow through as null, not throw or default to empty string.
            var messageId = Guid.NewGuid();
            var classification = await SeedClassificationAsync(messageId, topicVersion: null);

            var input = BuildInput(messageId);

            MessageResolutionAnalyzedDomainEvent published = null;
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<MessageResolutionAnalyzedDomainEvent, CancellationToken>((evt, _) => published = evt)
                .Returns(Task.CompletedTask);

            await _activity.PersistJudgmentAndReportAsync(classification, BuildAgent(), input, isResolved: true);

            Assert.NotNull(published);
            Assert.Null(published.TopicVersion);
        }

        [Fact]
        public async Task PersistJudgmentAndReportAsync_RowAlreadyJudgedConcurrently_SkipsUpdateAndNeverReports()
        {
            // Regression guard (Copilot PR review, US 7142): RunAsync's own read and this method's
            // ExecuteUpdateAsync are not atomic with each other - the backfill (or a duplicate scheduling of
            // this same activity) could judge the SAME message in between them. The UPDATE predicate itself
            // must enforce the dedup guard, not just the earlier read, or this path could overwrite an
            // already-persisted verdict and publish a second DXP event for the same message.
            var messageId = Guid.NewGuid();
            var classification = await SeedClassificationAsync(messageId);

            // Simulate the race: another path (backfill) judges the row AFTER this activity's own initial
            // read (the in-memory `classification` object below still reflects the pre-race, unjudged state).
            await _db.MessageTopicClassifications
                .Where(m => m.MessageId == messageId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsResolved, true), TestContext.Current.CancellationToken);

            var input = BuildInput(messageId);

            var updated = await _activity.PersistJudgmentAndReportAsync(classification, BuildAgent(), input, isResolved: false);

            Assert.False(updated);
            _mediatorMock.Verify(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);

            // The concurrently-written verdict (true) must survive untouched - never overwritten by this
            // activity's own (stale, isResolved: false) judgment.
            var persisted = await _db.MessageTopicClassifications.AsNoTracking().SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
            Assert.True(persisted.IsResolved);
        }

        [Fact]
        public async Task PersistJudgmentAndReportAsync_DxpPublishThrows_DoesNotPropagate()
        {
            var messageId = Guid.NewGuid();
            var classification = await SeedClassificationAsync(messageId);
            var input = BuildInput(messageId);

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<MessageResolutionAnalyzedDomainEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("DXP unreachable"));

            // Must not throw — the UPDATE already succeeded and must not be masked by a downstream DXP failure.
            var updated = await _activity.PersistJudgmentAndReportAsync(classification, BuildAgent(), input, isResolved: true);

            Assert.True(updated);

            var persisted = await _db.MessageTopicClassifications.AsNoTracking().SingleAsync(m => m.MessageId == messageId, TestContext.Current.CancellationToken);
            Assert.True(persisted.IsResolved);
        }
    }
}
