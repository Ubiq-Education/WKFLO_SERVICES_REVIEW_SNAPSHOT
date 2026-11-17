using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI.Providers;
using AMAIS.WKFLO.Common.Models.Settings;
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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// US 7959 / Task 7961: the agent run records why a turn failed (a code, never exception or type text) instead of
    /// messaging the chat itself: an unsupported provider is no longer sent as an answer, and a failed speech synthesis no
    /// longer puts the exception text in a ConversationStateUpdate.
    /// </summary>
    public class SemanticKernelAgentServiceChatErrorTests
    {
        private const string SessionId = "session-errors";
        private const string Sentinel = "SENTINEL-speech-secret";

        private readonly ChatNotificationRecorder _chat = new();
        private readonly ChatTurnFailureState _failureState = new();
        private readonly Mock<IAzureSpeechProvider> _speech = new();

        /// <summary>A provider type the kernel factory does not know.</summary>
        private sealed class UnknownTestProvider : AIBaseProvider
        {
        }

        private SemanticKernelAgentService BuildService()
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            return new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(),
                Mock.Of<IWkfloMapper>(),
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IEisKnowledgeProvider>(),
                _chat.Object,
                Mock.Of<IMcpClientService>(),
                Mock.Of<IWkfloMediator>(),
                Options.Create(new ElasticsearchSettings()),
                new TenantContext { TenantName = "acme" },
                new ChatContext { SessionId = SessionId },
                new UserContext(),
                Mock.Of<IBlobStorageProvider>(),
                _speech.Object,
                Mock.Of<IDateTimeProvider>(),
                db.Object,
                Mock.Of<IContactFormPluginLogService>(),
                chatTurnFailureState: _failureState);
        }

        private static TenantAgent Agent() => new()
        {
            Id = 42,
            Name = "Kit",
            AIModel = new AIModel { Provider = "custom", Model = "model-x" }
        };

        [Fact]
        public async Task RunText_UnsupportedProvider_ReturnsNoAnswer_RecordsConfigInvalid_AndStopsTheBubble()
        {
            var answer = await BuildService().RunTextAsync(new RunTextOptions
            {
                Agent = Agent(),
                Provider = new UnknownTestProvider(),
                SessionId = SessionId,
                Message = "hi"
            }, TestContext.Current.CancellationToken);

            Assert.Null(answer);
            Assert.Equal(ChatErrorCodes.ChatModelConfigInvalid, _failureState.Failure?.Code);
            Assert.Equal(42, _failureState.Failure.AgentId);
            Assert.Equal([false], _chat.TypingOf(42));
            Assert.DoesNotContain(_chat.Sent, s => s.Method == HubReceiverMethods.ReceiveMessage);
            _chat.AssertNoLeak(nameof(UnknownTestProvider), "not supported");
        }

        [Fact]
        public async Task RunVoice_UnsupportedProvider_ReturnsNoAnswer_RecordsConfigInvalid()
        {
            var answer = await BuildService().RunVoiceAsync(new RunVoiceOptions
            {
                Agent = Agent(),
                Provider = new UnknownTestProvider(),
                SessionId = SessionId,
                Message = "hi"
            }, TestContext.Current.CancellationToken);

            Assert.Null(answer);
            Assert.Equal(ChatErrorCodes.ChatModelConfigInvalid, _failureState.Failure?.Code);
            _chat.AssertNoLeak(nameof(UnknownTestProvider), "not supported");
        }

        private static async IAsyncEnumerable<StreamingChatMessageContent> Stream(string[] chunks, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk);
            }
        }

        private static Kernel KernelStreaming(params string[] chunks)
        {
            var chatCompletionService = new Mock<IChatCompletionService>();
            chatCompletionService
                .Setup(x => x.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .Returns(() => Stream(chunks));
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionService.Object);
            return builder.Build();
        }

        private static RunVoiceChatData VoiceData(Kernel kernel) => new()
        {
            Kernel = kernel,
            Agent = new TenantAgent { Id = 43, Name = "Voice Agent", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
            AgentTagNames = [],
            Prompt = "You are a helpful assistant.",
            Message = "hello",
            SessionId = SessionId,
            ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
            CreatedAt = DateTimeOffset.UtcNow,
            ChatName = "Voice chat",
            Voice = "en-US-JennyNeural",
            Language = "en-US"
        };

        [Fact]
        public async Task SpeechSynthesisFails_RecordsVoiceSynthesisFailed_WithoutSendingTheExceptionText()
        {
            _speech.Setup(x => x.SynthesizeSpeechAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException(Sentinel));

            var answer = await BuildService().RunVoiceChatCompletionAgentAsync(VoiceData(KernelStreaming("<p>Hello there, ", "how can I help?</p>")), TestContext.Current.CancellationToken);

            Assert.NotNull(answer);
            Assert.Equal(ChatErrorCodes.ChatVoiceSynthesisFailed, _failureState.Failure?.Code);
            Assert.True(_failureState.Failure.IsTerminal); // the text answer was not delivered yet
            Assert.Equal(43, _failureState.Failure.AgentId);
            Assert.False(_failureState.AnswerDelivered);

            // The raw ConversationStateUpdate "error" with the exception text is gone; nothing carries the exception.
            Assert.DoesNotContain(_chat.Sent, s => s.Method == HubReceiverMethods.ConversationStateUpdate);
            _chat.AssertNoLeak(Sentinel, "Audio synthesis failed", nameof(InvalidOperationException));

            // Nothing turns the failing agent's bubble back on after the failure (the run used to send typing-on here).
            Assert.DoesNotContain(true, _chat.TypingOf(43));
        }

        [Fact]
        public async Task VoiceAnswerDelivered_MarksTheAnswerDelivered_AndRecordsNothing()
        {
            _speech.Setup(x => x.SynthesizeSpeechAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([1, 2, 3]);

            await BuildService().RunVoiceChatCompletionAgentAsync(VoiceData(KernelStreaming("<p>Hello there.</p>")), TestContext.Current.CancellationToken);

            Assert.True(_failureState.AnswerDelivered);
            Assert.False(_failureState.HasFailure);
        }

        [Fact]
        public async Task TextAnswerDelivered_MarksTheAnswerDelivered()
        {
            var data = new RunChatData
            {
                Kernel = KernelStreaming("A full ", "answer."),
                Agent = new TenantAgent { Id = 42, Name = "Kit", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
                AgentTagNames = [],
                Prompt = "You are a helpful assistant.",
                Message = "hello",
                SessionId = SessionId,
                ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Chat",
                UserMessageId = Guid.NewGuid()
            };

            var answer = await BuildService().RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.Equal("A full answer.", answer.Message);
            Assert.True(_failureState.AnswerDelivered);
            Assert.False(_failureState.HasFailure);
        }
    }
}
