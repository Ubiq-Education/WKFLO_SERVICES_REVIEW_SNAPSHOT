using AMAIS.WKFLO.AI.Application.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
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

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Workflows
{
    /// <summary>
    /// US 7507. Real (temporary) SQLite database, same precedent as <see cref="ConversationAnalysisWorkflowActivityTests"/>:
    /// the atomic claim and the title write are ExecuteUpdateAsync calls, which MockQueryable cannot run.
    /// </summary>
    public class ConversationTitleWorkflowActivityTests : IAsyncDisposable
    {
        private const string SessionId = "sess-1";
        private const int AgentId = 5;

        private readonly SqliteConnection _connection;
        private readonly MasterDataDbContext _db;
        private readonly Mock<IChatNotificationService> _notificationServiceMock = new();
        private readonly Mock<IChatCompletionService> _chatCompletionMock = new();
        private readonly TestableConversationTitleWorkflowActivity _activity;

        public ConversationTitleWorkflowActivityTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            _connection.Open();

            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(_connection).Options;
            _db = new MasterDataDbContext(options, Mock.Of<IDateTimeProvider>());
            _db.Database.EnsureCreated();

            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(x => x.DateTimeOffsetUtcNow()).Returns(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

            _activity = new TestableConversationTitleWorkflowActivity(
                _db,
                _notificationServiceMock.Object,
                new ChatContext(),
                dateTimeProvider.Object,
                _chatCompletionMock.Object);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private sealed class TestableConversationTitleWorkflowActivity : ConversationTitleWorkflowActivity
        {
            private readonly IChatCompletionService _chatCompletionService;

            public TestableConversationTitleWorkflowActivity(MasterDataDbContext db, IChatNotificationService notificationService, ChatContext chatContext,
                IDateTimeProvider dateTimeProvider, IChatCompletionService chatCompletionService)
                : base(db, notificationService, chatContext, dateTimeProvider, NullLogger<ConversationTitleWorkflowActivity>.Instance)
            {
                _chatCompletionService = chatCompletionService;
            }

            public int ModelRequests { get; private set; }

            internal override IChatCompletionService CreateChatCompletionService(RunnableAgent agent)
            {
                ModelRequests++;
                return _chatCompletionService;
            }
        }

        private async Task SeedAsync(ConversationTitleStatus status = ConversationTitleStatus.Pending, bool agentIsActive = true)
        {
            _db.TenantAgents.Add(new TenantAgent { Id = AgentId, Name = "MAISY", IsActive = agentIsActive, TenantId = 1 });
            _db.Conversations.Add(new Conversation { SessionId = SessionId, AgentId = AgentId, TenantId = 1, Name = "Can you help me plan the trip?", TitleStatus = status });
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
            _db.ChangeTracker.Clear();
        }

        private void ModelAnswers(string content) =>
            _chatCompletionMock
                .Setup(x => x.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ChatMessageContent> { new(AuthorRole.Assistant, content) });

        private static ConversationTitleWorkflowInput Input(string firstUserMessage = "Can you help me plan the trip?") => new()
        {
            TenantName = "test-tenant",
            AgentId = AgentId,
            SessionId = SessionId,
            FirstUserMessage = firstUserMessage,
            FirstAssistantMessage = "Of course. Where are you going?"
        };

        private Task<Conversation> ReloadAsync() =>
            _db.Conversations.AsNoTracking().SingleAsync(c => c.SessionId == SessionId, TestContext.Current.CancellationToken);

        [Fact]
        public async Task RunAsync_WritesTheTitle_MarksGenerated_AndNotifiesTheChat()
        {
            await SeedAsync();
            ModelAnswers("\"Planning the school trip.\"");

            var result = await _activity.RunAsync(null, Input());

            Assert.True(result);
            var conversation = await ReloadAsync();
            Assert.Equal("Planning the school trip", conversation.Name);
            Assert.Equal(ConversationTitleStatus.Generated, conversation.TitleStatus);
            _notificationServiceMock.Verify(x => x.SendConversationTitleUpdatedAsync(SessionId, "Planning the school trip"), Times.Once);
        }

        [Theory]
        [InlineData(ConversationTitleStatus.Processing)]
        [InlineData(ConversationTitleStatus.Generated)]
        [InlineData(ConversationTitleStatus.Skipped)]
        [InlineData(ConversationTitleStatus.Failed)]
        public async Task RunAsync_NotPending_DoesNothing(ConversationTitleStatus status)
        {
            // The atomic claim is what makes the title run once, even when two workflows race for the same conversation.
            await SeedAsync(status);
            ModelAnswers("Another title");

            await _activity.RunAsync(null, Input());

            var conversation = await ReloadAsync();
            Assert.Equal(status, conversation.TitleStatus);
            Assert.Equal("Can you help me plan the trip?", conversation.Name);
            Assert.Equal(0, _activity.ModelRequests);
        }

        [Fact]
        public async Task RunAsync_SecondRun_DoesNotCallTheModelAgain()
        {
            await SeedAsync();
            ModelAnswers("Planning the school trip");

            await _activity.RunAsync(null, Input());
            await _activity.RunAsync(null, Input());

            Assert.Equal(1, _activity.ModelRequests);
        }

        [Fact]
        public async Task RunAsync_ModelThrows_MarksFailed_KeepsTheName_AndDoesNotThrow()
        {
            await SeedAsync();
            _chatCompletionMock
                .Setup(x => x.GetChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("model unavailable"));

            var result = await _activity.RunAsync(null, Input());

            Assert.True(result);
            var conversation = await ReloadAsync();
            Assert.Equal(ConversationTitleStatus.Failed, conversation.TitleStatus);
            Assert.Equal("Can you help me plan the trip?", conversation.Name);
            _notificationServiceMock.Verify(x => x.SendConversationTitleUpdatedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// The activity never throws: a database failure on the initial claim is logged and the run ends with the
        /// documented result, without calling the model.
        /// </summary>
        [Fact]
        public async Task RunAsync_ClaimFails_DoesNotThrow_AndDoesNotCallTheModel()
        {
            await SeedAsync();
            await _db.Database.ExecuteSqlRawAsync("DROP TABLE [Conversation]", TestContext.Current.CancellationToken);

            var result = await _activity.RunAsync(null, Input());

            Assert.True(result);
            Assert.Equal(0, _activity.ModelRequests);
            _notificationServiceMock.Verify(x => x.SendConversationTitleUpdatedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RunAsync_NoInput_DoesNotThrow()
        {
            var result = await _activity.RunAsync(null, null);

            Assert.True(result);
            Assert.Equal(0, _activity.ModelRequests);
        }

        [Fact]
        public async Task RunAsync_EmptyModelAnswer_MarksFailed()
        {
            await SeedAsync();
            ModelAnswers("  \n ");

            await _activity.RunAsync(null, Input());

            Assert.Equal(ConversationTitleStatus.Failed, (await ReloadAsync()).TitleStatus);
        }

        [Fact]
        public async Task RunAsync_InactiveAgent_MarksFailed_WithoutCallingTheModel()
        {
            await SeedAsync(agentIsActive: false);

            await _activity.RunAsync(null, Input());

            Assert.Equal(ConversationTitleStatus.Failed, (await ReloadAsync()).TitleStatus);
            Assert.Equal(0, _activity.ModelRequests);
        }

        [Theory]
        [InlineData("Planning the school trip", "Planning the school trip")]
        [InlineData("\"Planning the school trip.\"", "Planning the school trip")]
        [InlineData("Title: Planning the school trip", "Planning the school trip")]
        [InlineData("**Planning the school trip**", "Planning the school trip")]
        [InlineData("\n\nPlanning the school trip\nSome explanation", "Planning the school trip")]
        [InlineData("“Planning the school trip?”", "Planning the school trip")]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("\"\"", null)]
        public void SanitizeTitle_CleansUpTheModelAnswer(string content, string expected)
        {
            Assert.Equal(expected, ConversationTitleWorkflowActivity.SanitizeTitle(content));
        }

        [Fact]
        public void SanitizeTitle_CapsTheLength()
        {
            var title = ConversationTitleWorkflowActivity.SanitizeTitle(new string('a', 200));

            Assert.Equal(ConversationTitleWorkflowActivity.MaxTitleLength, title.Length);
        }

        [Fact]
        public void BuildTitlePrompt_HasTheInstructions_AndTheCutExchange()
        {
            var prompt = ConversationTitleWorkflowActivity.BuildTitlePrompt(new string('u', 1500), "Of course.");

            Assert.Equal(AuthorRole.System, prompt[0].Role);
            Assert.Equal(ConversationTitleWorkflowActivity.TitleSystemPrompt, prompt[0].Content);
            var exchange = prompt[1].Content;
            Assert.StartsWith("User: " + new string('u', ConversationTitleWorkflowActivity.MaxPromptMessageLength) + "\n", exchange);
            Assert.EndsWith("Assistant: Of course.", exchange);
        }
    }
}
