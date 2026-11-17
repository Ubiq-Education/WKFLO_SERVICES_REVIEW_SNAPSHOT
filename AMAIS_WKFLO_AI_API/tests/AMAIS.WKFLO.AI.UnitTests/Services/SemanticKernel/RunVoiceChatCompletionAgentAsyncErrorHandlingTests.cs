using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
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
    public class RunVoiceChatCompletionAgentAsyncErrorHandlingTests
    {
        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly ChatTurnFailureState _failureState = new();
        private readonly SemanticKernelAgentService _service;

        public RunVoiceChatCompletionAgentAsyncErrorHandlingTests()
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
        /// US 7959 / Task 7961: a streaming failure is recorded as a code for the turn's choke point; no code-less error
        /// bubble, and the failing agent's thinking bubble is not turned back on.
        /// </summary>
        [Fact]
        public async Task RunVoiceChatCompletionAgentAsync_WhenStreamingThrows_RecordsTheFailureWithoutSendingABubbleAndReturnsEmptyAnswer()
        {
            // Arrange
            var kernel = BuildKernelWithThrowingStreamingChatCompletionService();
            var agent = new TenantAgent
            {
                Id = 43,
                Name = "Voice Agent",
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };

            var data = new RunVoiceChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = new List<string>(),
                Prompt = "You are a helpful assistant.",
                Message = "hello",
                SessionId = "session-voice-789",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Voice Chat",
                Voice = "en-US-JennyNeural",
                Language = "en-US"
            };

            // Act
            var result = await _service.RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            // Assert: the method still returns an AgentAnswer (not null) with no assistant text,
            // so callers relying on an `if (!string.IsNullOrEmpty(agentAnswer.Message))` guard skip
            // sending a duplicate bubble, and no partial/broken narration gets synthesized.
            Assert.NotNull(result);
            Assert.Equal(string.Empty, result.Message);

            _notificationServiceMock.Verify(
                x => x.SendMessageToChat(It.IsAny<string>(), It.IsAny<ChatMessage>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _notificationServiceMock.Verify(
                x => x.SendTypingIndicator(It.IsAny<string>(), true, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);

            Assert.Equal(ChatErrorCodes.ChatTurnFailed, _failureState.Failure?.Code);
            Assert.Equal(43, _failureState.Failure.AgentId);
            Assert.False(_failureState.AnswerDelivered);

            // The failure path clears any partial content, so the finalizing SendStreamingTokens call
            // carries an empty Tokens array — it still fires with isComplete: true to close out the
            // streaming placeholder for this messageId.
            _notificationServiceMock.Verify(
                x => x.SendStreamingTokens(
                    "session-voice-789",
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.Is<List<string>>(tokens => tokens.Count == 0),
                    AuthorRole.Assistant,
                    ChannelMessageType.Normal,
                    true,
                    43,
                    "Voice Agent",
                    It.IsAny<string>()),
                Times.Once);

            // No partial/broken sentence fragment should be synthesized as narration after a failure —
            // the buffer is discarded, so the "flush remaining buffer" fallback sends a silent final marker.
            _notificationServiceMock.Verify(
                x => x.SendAudioChunkAsync("session-voice-789", string.Empty, string.Empty, true),
                Times.Once);
        }
    }
}
