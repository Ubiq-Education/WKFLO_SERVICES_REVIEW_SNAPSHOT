using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    public class RunChatClassificationAsyncTests
    {
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly SemanticKernelAgentService _service;
        private readonly List<MessageTopicClassification> _persistedClassifications = [];

        private static readonly Guid ActiveTopicId = Guid.NewGuid();
        private const string SessionId = "session-xyz";
        private const int AgentId = 7;
        private const string TenantName = "test-tenant";
        private const string SubjectId = "subj-xyz";

        public RunChatClassificationAsyncTests()
        {
            _dbMock = new Mock<IMasterDataDbContext>();
            _mediatorMock = new Mock<IWkfloMediator>();

            var classificationsDbSet = _persistedClassifications.BuildMockDbSet();
            classificationsDbSet.Setup(x => x.Add(It.IsAny<MessageTopicClassification>()))
                .Callback<MessageTopicClassification>(m => _persistedClassifications.Add(m));

            _dbMock.Setup(x => x.MessageTopicClassifications).Returns(classificationsDbSet.Object);
            _dbMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

            _service = new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(),
                Mock.Of<IWkfloMapper>(),
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(),
                Mock.Of<IMcpClientService>(),
                _mediatorMock.Object,
                Options.Create(new ElasticsearchSettings()),
                new TenantContext { TenantName = TenantName },
                new ChatContext { SubjectId = SubjectId },
                new UserContext(),
                Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(),
                dateTimeProviderMock.Object,
                _dbMock.Object,
                Mock.Of<IContactFormPluginLogService>());
        }

        private static Agent BuildAgentWithClassificationTool()
        {
            var config = $$"""
                {"topics":[{"id":"{{ActiveTopicId}}","name":"Billing","description":"Billing questions","isActive":true,"handoffMessage":"Routing to billing"}]}
                """;

            var tool = new TenantTool
            {
                Id = 1,
                Type = "Chat_Classification",
                Config = config,
                IsActive = true,
                IsDeleted = false
            };

            return new TenantAgent
            {
                Id = AgentId,
                AgentTools = [new AgentTool { AgentId = AgentId, ToolId = 1, Tool = tool }]
            };
        }

        private static Kernel BuildKernelWithChatCompletionService(string rawResponse)
        {
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            IReadOnlyList<ChatMessageContent> response = [new ChatMessageContent(AuthorRole.Assistant, rawResponse)];

            chatCompletionServiceMock.Setup(x => x.GetChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            return builder.Build();
        }

        [Theory]
        [InlineData("NONE")]
        [InlineData("")]
        [InlineData("not-a-guid")]
        public async Task RunChatClassificationAsync_NoMatch_PersistsUncategorisedRowAndReturnsNullTuple(string rawResponse)
        {
            // Arrange
            var agent = BuildAgentWithClassificationTool();
            var kernel = BuildKernelWithChatCompletionService(rawResponse);
            var messageId = Guid.NewGuid();

            // Act
            var (topicName, handoffMessage) = await _service.RunChatClassificationAsync(
                kernel, agent, "some user message", messageId, SessionId, false, CancellationToken.None);

            // Assert
            Assert.Null(topicName);
            Assert.Null(handoffMessage);

            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(messageId, persisted.MessageId);
            Assert.Null(persisted.TopicId);
            Assert.Null(persisted.TopicName);
            Assert.Equal(AgentId, persisted.AgentId);
            Assert.Equal(SessionId, persisted.SessionId);
        }

        [Fact]
        public async Task RunChatClassificationAsync_UnknownTopicId_PersistsUncategorisedRow()
        {
            // Arrange: syntactically valid GUID that isn't one of the agent's active topics
            var agent = BuildAgentWithClassificationTool();
            var kernel = BuildKernelWithChatCompletionService(Guid.NewGuid().ToString());
            var messageId = Guid.NewGuid();

            // Act
            var (topicName, handoffMessage) = await _service.RunChatClassificationAsync(
                kernel, agent, "some user message", messageId, SessionId, false, CancellationToken.None);

            // Assert
            Assert.Null(topicName);
            Assert.Null(handoffMessage);
            var persisted = Assert.Single(_persistedClassifications);
            Assert.Null(persisted.TopicId);
            Assert.Null(persisted.TopicName);
        }

        [Fact]
        public async Task RunChatClassificationAsync_MatchedTopic_PersistsRowAndReturnsTopicNameAndHandoffMessage()
        {
            // Arrange
            var agent = BuildAgentWithClassificationTool();
            var kernel = BuildKernelWithChatCompletionService(ActiveTopicId.ToString());
            var messageId = Guid.NewGuid();

            // Act
            var (topicName, handoffMessage) = await _service.RunChatClassificationAsync(
                kernel, agent, "how do I pay my invoice?", messageId, SessionId, false, CancellationToken.None);

            // Assert
            Assert.Equal("Billing", topicName);
            Assert.Equal("Routing to billing", handoffMessage);

            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(ActiveTopicId, persisted.TopicId);
            Assert.Equal("Billing", persisted.TopicName);
            // US 7142: the row's TopicVersion must be set from the same topicVersion already resolved and
            // reported to DXP at classification time — not left null.
            Assert.False(string.IsNullOrEmpty(persisted.TopicVersion));

            // US 7142 (later re-scope): classification must NOT report anything to DXP by itself — a message
            // gets exactly ONE "Chatbot Message Topic" event, sent once the resolution judgment is known (see
            // MessageResolutionWorkflowActivity), not a premature one here plus a second one later.
            Assert.Empty(_mediatorMock.Invocations);
        }

        [Fact]
        public async Task RunChatClassificationAsync_ContactFormReplay_SkipsClassificationAndDoesNotPersistOrPublish()
        {
            // A ContactForm "replay" turn resends the exact same question already classified during the
            // original turn — classifying it again would fire a second, redundant "Chatbot Message
            // Topic" event for one visitor question. The guard must short-circuit before touching the
            // kernel/DB/mediator at all, so this test's kernel is never even given a chat completion
            // service — if the guard didn't short-circuit, resolving IChatCompletionService from an
            // empty kernel would throw.
            var agent = BuildAgentWithClassificationTool();
            var kernel = Kernel.CreateBuilder().Build();
            var messageId = Guid.NewGuid();

            var (topicName, handoffMessage) = await _service.RunChatClassificationAsync(
                kernel, agent, "what are your hours?", messageId, SessionId, true, CancellationToken.None);

            Assert.Null(topicName);
            Assert.Null(handoffMessage);
            Assert.Empty(_persistedClassifications);
            _dbMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(_mediatorMock.Invocations);
        }

        [Fact]
        public async Task RunChatClassificationAsync_DuplicateCallForSameMessage_DoesNotThrowAndDoesNotDuplicateRow()
        {
            // Arrange: simulate the real-time path (no delete-first guard) being invoked twice for the
            // same message — e.g. a retried request — now that MessageId is uniquely constrained.
            var agent = BuildAgentWithClassificationTool();
            var messageId = Guid.NewGuid();

            var firstKernel = BuildKernelWithChatCompletionService(ActiveTopicId.ToString());
            var firstResult = await _service.RunChatClassificationAsync(
                firstKernel, agent, "how do I pay my invoice?", messageId, SessionId, false, CancellationToken.None);

            // Act: second call for the exact same messageId should be treated as a no-op insert,
            // not throw, and not create a second row.
            var secondKernel = BuildKernelWithChatCompletionService(ActiveTopicId.ToString());
            var exception = await Record.ExceptionAsync(() => _service.RunChatClassificationAsync(
                secondKernel, agent, "how do I pay my invoice?", messageId, SessionId, false, CancellationToken.None));

            // Assert
            Assert.Null(exception);
            Assert.Equal("Billing", firstResult.topicName);
            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal(messageId, persisted.MessageId);
            _dbMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PersistClassificationOutcomeAsync_PersistsTheGivenTopicVersionOnTheRow()
        {
            // Arrange
            var messageId = Guid.NewGuid();
            var agent = BuildAgentWithClassificationTool();

            // Act
            await _service.PersistClassificationOutcomeAsync(ActiveTopicId, "Billing", SessionId, messageId, agent, "topic-version-42", CancellationToken.None);

            // Assert
            var persisted = Assert.Single(_persistedClassifications);
            Assert.Equal("topic-version-42", persisted.TopicVersion);
        }

        [Fact]
        public async Task PersistClassificationOutcomeAsync_ConcurrentInsertRace_SwallowsDbUpdateException()
        {
            // Arrange: force the "already classified?" check to report false (simulating a race where
            // another request wins the check), then make SaveChangesAsync throw the unique-constraint
            // violation that would occur in SQL Server — this must be caught and swallowed, not propagate.
            _dbMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("Violation of UNIQUE KEY constraint 'IX_MessageTopicClassification_MessageId'"));

            var messageId = Guid.NewGuid();

            // Act
            var agent = BuildAgentWithClassificationTool();
            var exception = await Record.ExceptionAsync(() =>
                _service.PersistClassificationOutcomeAsync(ActiveTopicId, "Billing", SessionId, messageId, agent, "topic-version-1", CancellationToken.None));

            // Assert
            Assert.Null(exception);
        }
    }
}
