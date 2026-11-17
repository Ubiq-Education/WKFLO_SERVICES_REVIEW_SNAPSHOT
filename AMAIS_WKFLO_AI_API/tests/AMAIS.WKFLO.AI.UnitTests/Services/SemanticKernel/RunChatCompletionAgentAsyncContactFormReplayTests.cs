using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
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
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// A ContactForm "replay" turn (RunChatData.IsContactFormReplay) must not be recorded as a second,
    /// independent visitor message — no duplicate AuthorRole.User entry for the already-recorded
    /// question, and no second topic classification (covered separately in
    /// RunChatClassificationAsyncTests). The model still runs and produces a fresh answer.
    ///
    /// The original turn's ContactForm-tool filler/registration reply must be PRESERVED in persisted
    /// history, not removed — deleting it to "supersede" it was tried and rejected, since it silently
    /// dropped a real message from Chat Logs. The correct persisted shape is 3 messages in chronological
    /// order: the original User question, the original filler Assistant reply, and the new real
    /// Assistant reply.
    /// </summary>
    public class RunChatCompletionAgentAsyncContactFormReplayTests
    {
        private const string FillerText = "A registration form has been displayed to the user. Registration is optional to continue.";
        private const string OriginalQuestion = "what are your hours?";

        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly Mock<IMessageResolutionWorkflowScheduler> _messageResolutionWorkflowSchedulerMock;
        private readonly Mock<IContactFormReplayStateService> _contactFormReplayStateServiceMock;
        private readonly ChatContext _chatContext;
        private readonly SemanticKernelAgentService _service;
        private ChatHistoryUpdatedDomainEvent _publishedEvent;

        public RunChatCompletionAgentAsyncContactFormReplayTests()
        {
            var dbMock = new Mock<IMasterDataDbContext>();
            dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            dbMock.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>().BuildMockDbSet().Object);

            _mediatorMock = new Mock<IWkfloMediator>();
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<ChatHistoryUpdatedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<ChatHistoryUpdatedDomainEvent, CancellationToken>((evt, _) => _publishedEvent = evt)
                .Returns(Task.CompletedTask);

            _messageResolutionWorkflowSchedulerMock = new Mock<IMessageResolutionWorkflowScheduler>();
            _contactFormReplayStateServiceMock = new Mock<IContactFormReplayStateService>();
            _chatContext = new ChatContext();

            _service = new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(),
                Mock.Of<IWkfloMapper>(),
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(),
                Mock.Of<IMcpClientService>(),
                _mediatorMock.Object,
                Options.Create(new ElasticsearchSettings()),
                new TenantContext(),
                _chatContext,
                new UserContext(),
                Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(),
                dbMock.Object,
                Mock.Of<IContactFormPluginLogService>(),
                messageResolutionWorkflowScheduler: _messageResolutionWorkflowSchedulerMock.Object,
                contactFormReplayStateService: _contactFormReplayStateServiceMock.Object);
        }

        private static async IAsyncEnumerable<StreamingChatMessageContent> StreamingContents(params string[] chunks)
        {
            await Task.CompletedTask;
            foreach (var chunk in chunks)
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk);
        }

        private static Kernel BuildKernelReturning(string[] chunks, out Func<ChatHistory> getCapturedModelInput)
        {
            ChatHistory capturedHistory = null;

            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock
                .Setup(x => x.GetStreamingChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ChatHistory, PromptExecutionSettings, Kernel, CancellationToken>((h, _, _, _) => capturedHistory = new ChatHistory(h))
                .Returns(StreamingContents(chunks));

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);

            getCapturedModelInput = () => capturedHistory;
            return builder.Build();
        }

        private static ChatMessageContent BuildOriginalUserQuestion() =>
            new(AuthorRole.User, OriginalQuestion, metadata: new Dictionary<string, object>
            {
                { CommonMetadataConsts.Timestamp, DateTimeOffset.UtcNow.AddMinutes(-1) },
                { CommonMetadataConsts.MessageId, Guid.NewGuid().ToString() }
            });

        private static ChatMessageContent BuildFillerAssistantReply() =>
            new(AuthorRole.Assistant, FillerText, metadata: new Dictionary<string, object>
            {
                { CommonMetadataConsts.Timestamp, DateTimeOffset.UtcNow.AddSeconds(-30) },
                { CommonMetadataConsts.MessageId, Guid.NewGuid().ToString() }
            });

        [Fact]
        public async Task RunChatCompletionAgentAsync_ContactFormReplay_PreservesFillerAndAppendsRealAnswerWithoutDuplicatingQuestion()
        {
            // Arrange: history as left by turn 1 — the original question, then the ContactForm tool's
            // filler assistant reply.
            var history = new ChatHistory { BuildOriginalUserQuestion(), BuildFillerAssistantReply() };

            var kernel = BuildKernelReturning(["We are open 9-5, Monday through Friday."], out var getCapturedModelInput);
            var agent = new TenantAgent
            {
                Id = 3,
                Name = "Support Bot",
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };

            var data = new RunChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = [],
                Prompt = "You are a helpful assistant.",
                Message = OriginalQuestion, // wkflo_ui replays with the literal original question text
                SessionId = "session-replay",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(history),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = Guid.NewGuid(),
                IsContactFormReplay = true
            };

            await _service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.NotNull(_publishedEvent);

            // Corrected persisted shape: exactly 3 messages, in chronological order — original question,
            // original filler, new real answer. No duplicate visitor message, filler intact.
            var persisted = _publishedEvent.ChatHistory.ToList();
            Assert.Equal(3, persisted.Count);
            Assert.Equal(AuthorRole.User, persisted[0].Role);
            Assert.Equal(OriginalQuestion, persisted[0].Content);
            Assert.Equal(AuthorRole.Assistant, persisted[1].Role);
            Assert.Equal(FillerText, persisted[1].Content);
            Assert.Equal(AuthorRole.Assistant, persisted[2].Role);
            Assert.Equal("We are open 9-5, Monday through Friday.", persisted[2].Content);

            // The ephemeral replay cue used to make the model call valid must never reach the persisted
            // history — it's filtered by BuildPersistableChatHistory, same as
            // LoadSiblingAgentContextBySessionQueryHandler's sibling-context injection.
            Assert.DoesNotContain(persisted, m => m.Metadata != null && m.Metadata.ContainsKey(CommonMetadataConsts.IsEphemeralAgentContext));

            // The model call itself must still be valid: Anthropic's Messages API requires the message
            // list to end in a 'user' turn, and the history here ends in the filler Assistant reply
            // before any cue is added — so the ephemeral cue must actually reach the model as the last
            // message, proving the completion request is well-formed regardless of provider.
            var modelInput = getCapturedModelInput();
            Assert.NotNull(modelInput);
            Assert.Equal(AuthorRole.User, modelInput.Last().Role);
            Assert.Equal(OriginalQuestion, modelInput.Last().Content);
        }

        [Fact]
        public async Task RunChatCompletionAgentAsync_ContactFormReplayWithReasoningAndImageAnalysis_StillEndsModelInputInEphemeralCue()
        {
            // Regression guard for the Copilot-flagged ordering bug: the ephemeral replay cue must be
            // added AFTER the reasoning/image-analysis message blocks, not before, so a replay turn that
            // also produces non-empty Chain-of-Thought reasoning or image-analysis output doesn't end up
            // with those Assistant-authored messages landing after the cue — which would put the model
            // call back into the exact "must end in a user turn" problem the cue exists to solve.
            var history = new ChatHistory { BuildOriginalUserQuestion(), BuildFillerAssistantReply() };

            var kernel = BuildKernelReturning(["We are open 9-5, Monday through Friday."], out var getCapturedModelInput);
            var agent = new TenantAgent
            {
                Id = 5,
                Name = "Support Bot",
                AIModel = new AIModel { Provider = "anthropic", Model = "claude-3-5-sonnet" }
            };

            var data = new RunChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = [],
                Prompt = "You are a helpful assistant.",
                Message = OriginalQuestion,
                SessionId = "session-replay-reasoning",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(history),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = Guid.NewGuid(),
                IsContactFormReplay = true,
                AssitantReasoning = "Thinking about the visitor's question before answering...",
                ImageAnalysis = "No image was attached to this turn."
            };

            await _service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            // The model call must still end in the ephemeral User-role cue, even though this turn also
            // appended two extra Assistant-authored messages (reasoning, image analysis) in between.
            var modelInput = getCapturedModelInput();
            Assert.NotNull(modelInput);
            Assert.Equal(AuthorRole.User, modelInput.Last().Role);
            Assert.Equal(OriginalQuestion, modelInput.Last().Content);

            // The reasoning/image-analysis messages are still present, just before the cue, not after it.
            var beforeCue = modelInput.ToList();
            Assert.Contains(beforeCue, m => m.Role == AuthorRole.Assistant && m.Content == data.AssitantReasoning);
            Assert.Contains(beforeCue, m => m.Role == AuthorRole.Assistant && m.Content == data.ImageAnalysis);
        }

        [Fact]
        public async Task RunChatCompletionAgentAsync_NonReplayTurn_StillAddsNewUserMessageAsUsual()
        {
            // Regression guard: an ordinary (non-replay) turn must be completely unaffected — the new
            // user message is still recorded, and no history is removed or ephemeral cue injected.
            var history = new ChatHistory { BuildOriginalUserQuestion(), BuildFillerAssistantReply() };

            var kernel = BuildKernelReturning(["Sure, here is more detail."], out _);
            var agent = new TenantAgent
            {
                Id = 4,
                Name = "Support Bot",
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };

            var data = new RunChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = [],
                Prompt = "You are a helpful assistant.",
                Message = "and on weekends?",
                SessionId = "session-normal",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(history),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = Guid.NewGuid(),
                IsContactFormReplay = false
            };

            await _service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.NotNull(_publishedEvent);

            // Both the original question/filler and the new turn's own user message survive untouched.
            Assert.Contains(_publishedEvent.ChatHistory, m => m.Role == AuthorRole.User && m.Content == OriginalQuestion);
            Assert.Contains(_publishedEvent.ChatHistory, m => m.Content != null && m.Content.Contains(FillerText));
            Assert.Contains(_publishedEvent.ChatHistory, m => m.Role == AuthorRole.User && m.Content == "and on weekends?");

            // Exactly 4 messages: original question, filler, this turn's new question, this turn's answer.
            Assert.Equal(4, _publishedEvent.ChatHistory.Count());
        }

        [Fact]
        public async Task RunChatCompletionAgentAsync_ContactFormRequestedThisTurn_SkipsResolutionSchedulingAndRecordsDeferredQuestion()
        {
            // US 7142 corner case: when the ContactForm tool fires this turn, the assistant's "answer" is
            // just the registration-required filler, not a real response to the user's question -
            // MessageResolutionWorkflow must not be scheduled for it (it would judge the filler as
            // "unresolved" and permanently lock in the wrong Response). Instead, this question's MessageId
            // must be recorded so the eventual replay turn can reuse it.
            _chatContext.ContactFormRequestedThisTurn = true;

            var history = new ChatHistory { BuildOriginalUserQuestion() };
            var kernel = BuildKernelReturning(["A registration form has been displayed to the user."], out _);
            var agent = new TenantAgent
            {
                Id = 6,
                Name = "Support Bot",
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };
            var userMessageId = Guid.NewGuid();

            var data = new RunChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = [],
                Prompt = "You are a helpful assistant.",
                Message = OriginalQuestion,
                SessionId = "session-contact-form-requested",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(history),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = userMessageId,
                IsContactFormReplay = false
            };

            await _service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            _messageResolutionWorkflowSchedulerMock.Verify(
                x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()), Times.Never);
            _contactFormReplayStateServiceMock.Verify(
                x => x.RecordDeferredQuestionAsync("session-contact-form-requested", userMessageId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunChatCompletionAgentAsync_ContactFormNotRequestedThisTurn_SchedulesResolutionNormally()
        {
            // Regression guard: an ordinary turn (ContactFormRequestedThisTurn left false, the default)
            // must keep scheduling MessageResolutionWorkflow as before, and must never record a deferred
            // question.
            Assert.False(_chatContext.ContactFormRequestedThisTurn);

            var history = new ChatHistory { BuildOriginalUserQuestion() };
            var kernel = BuildKernelReturning(["We are open 9-5, Monday through Friday."], out _);
            var agent = new TenantAgent
            {
                Id = 7,
                Name = "Support Bot",
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };

            var data = new RunChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = [],
                Prompt = "You are a helpful assistant.",
                Message = OriginalQuestion,
                SessionId = "session-normal-resolution",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(history),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = Guid.NewGuid(),
                IsContactFormReplay = false
            };

            await _service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            _messageResolutionWorkflowSchedulerMock.Verify(
                x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()), Times.Once);
            _contactFormReplayStateServiceMock.Verify(
                x => x.RecordDeferredQuestionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
