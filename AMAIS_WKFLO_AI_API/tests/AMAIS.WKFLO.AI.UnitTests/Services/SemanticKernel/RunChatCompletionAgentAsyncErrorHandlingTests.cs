using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    public class RunChatCompletionAgentAsyncErrorHandlingTests
    {
        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly ChatTurnFailureState _failureState = new();
        private readonly SemanticKernelAgentService _service;

        public RunChatCompletionAgentAsyncErrorHandlingTests()
        {
            _notificationServiceMock = new Mock<IChatNotificationService>();

            _dbMock = new Mock<IMasterDataDbContext>();
            var agentChatSettingsDbSet = new List<AgentChatSetting>().BuildMockDbSet();
            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettingsDbSet.Object);

            _service = new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(),
                Mock.Of<IWkfloMapper>(),
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IEisKnowledgeProvider>(),
                _notificationServiceMock.Object,
                Mock.Of<IMcpClientService>(),
                Mock.Of<IWkfloMediator>(),
                Options.Create(new ElasticsearchSettings()),
                new TenantContext(),
                new ChatContext(),
                new UserContext(),
                Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(),
                _dbMock.Object,
                Mock.Of<IContactFormPluginLogService>(),
                chatTurnFailureState: _failureState);
        }

        private static Kernel BuildKernelWithThrowingStreamingChatCompletionService()
        {
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock.Setup(x => x.GetStreamingChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Simulated streaming failure"));

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            return builder.Build();
        }

        /// <summary>
        /// US 7501: a Stop cancels the turn's token. Whatever the provider then throws (its own cancellation or a
        /// wrapped one), the turn is not an error: no error bubble, and the cancellation reaches the workflow activity.
        /// US 7502: the only message sent is the "Stopped" marker (see RunChatCompletionAgentAsyncStopTests).
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RunChatCompletionAgentAsync_WhenStoppedDuringStreaming_ThrowsCancellationWithoutErrorMessage(bool providerThrowsCancellation)
        {
            using var stop = new CancellationTokenSource();

            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock.Setup(x => x.GetStreamingChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .Callback(() => stop.Cancel()) // the Stop lands while the model is streaming
                .Throws(providerThrowsCancellation ? new OperationCanceledException(stop.Token) : new InvalidOperationException("request aborted"));
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);

            var data = new RunChatData
            {
                Kernel = builder.Build(),
                Agent = new TenantAgent { Id = 42, Name = "Test Agent", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
                AgentTagNames = new List<string>(),
                Prompt = "You are a helpful assistant.",
                Message = "hello",
                SessionId = "session-789",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = Guid.NewGuid()
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.RunChatCompletionAgentAsync(data, stop.Token));

            _notificationServiceMock.Verify(
                x => x.SendMessageToChat(It.IsAny<string>(), It.Is<ChatMessage>(m => m.Content != StoppedTurnMarker.Text), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _notificationServiceMock.Verify(
                x => x.SendMessageToChat("session-789", It.Is<ChatMessage>(m => m.Content == StoppedTurnMarker.Text), 42, "Test Agent", It.IsAny<string>()),
                Times.Once);
        }

        /// <summary>
        /// US 7959 / Task 7961: a streaming failure is recorded as a code for the turn's choke point (which tells the chat
        /// once); the run no longer sends its own code-less error bubble, and the exception text never reaches the chat.
        /// </summary>
        [Fact]
        public async Task RunChatCompletionAgentAsync_WhenStreamingThrows_RecordsTheFailureWithoutSendingABubbleAndReturnsEmptyAnswer()
        {
            // Arrange
            var kernel = BuildKernelWithThrowingStreamingChatCompletionService();
            var agent = new TenantAgent
            {
                Id = 42,
                Name = "Test Agent",
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };

            var data = new RunChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = new List<string>(),
                Prompt = "You are a helpful assistant.",
                Message = "hello",
                SessionId = "session-789",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = Guid.NewGuid()
            };

            // Act
            var result = await _service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            // Assert: the method still returns an AgentAnswer (not null) with no assistant text,
            // so callers like RunnableAISyncService.RunRunnableAgentAsync's
            // `if (!string.IsNullOrEmpty(agentAnswer.Message))` guard skips sending a duplicate bubble.
            Assert.NotNull(result);
            Assert.Equal(string.Empty, result.Message);

            _notificationServiceMock.Verify(
                x => x.SendMessageToChat(It.IsAny<string>(), It.IsAny<ChatMessage>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);

            Assert.True(_failureState.HasFailure);
            Assert.Equal(ChatErrorCodes.ChatTurnFailed, _failureState.Failure.Code);
            Assert.True(_failureState.Failure.IsTerminal);
            Assert.Equal(42, _failureState.Failure.AgentId);
            Assert.Equal("Test Agent", _failureState.Failure.AgentName);
            Assert.False(_failureState.AnswerDelivered);

            // The failure path clears any partial content, so the finalizing SendStreamingTokens call
            // carries an empty Tokens array — it still fires with isComplete: true to close out the
            // streaming placeholder for this messageId.
            _notificationServiceMock.Verify(
                x => x.SendStreamingTokens(
                    "session-789",
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.Is<List<string>>(tokens => tokens.Count == 0),
                    AuthorRole.Assistant,
                    ChannelMessageType.Normal,
                    true,
                    42,
                    "Test Agent",
                    It.IsAny<string>()),
                Times.Once);
        }
    }
}
