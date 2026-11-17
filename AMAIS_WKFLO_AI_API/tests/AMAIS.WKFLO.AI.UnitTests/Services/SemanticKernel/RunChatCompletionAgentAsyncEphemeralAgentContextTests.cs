using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
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
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Covers US 7078: content injected ephemerally by LoadSiblingAgentContextBySessionQueryHandler
    /// (marked via CommonMetadataConsts.IsEphemeralAgentContext) must be visible to the model for this
    /// turn's call, but must never reach the ChatHistoryUpdatedDomainEvent that gets persisted via
    /// SaveChatAsync — see SemanticKernelAgentService.BuildPersistableChatHistory.
    /// </summary>
    public class RunChatCompletionAgentAsyncEphemeralAgentContextTests
    {
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly SemanticKernelAgentService _service;
        private ChatHistoryUpdatedDomainEvent _publishedEvent;

        public RunChatCompletionAgentAsyncEphemeralAgentContextTests()
        {
            var dbMock = new Mock<IMasterDataDbContext>();
            dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            // Agent.EnableSpinUpAgents = true also triggers BuildAgentEventPublisherInjectionAsync's own
            // (unrelated, pre-existing) AgentAllowedSpinUpAgent lookup — must be set up even though these
            // tests aren't exercising that feature.
            dbMock.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>().BuildMockDbSet().Object);

            _mediatorMock = new Mock<IWkfloMediator>();
            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<ChatHistoryUpdatedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback<ChatHistoryUpdatedDomainEvent, CancellationToken>((evt, _) => _publishedEvent = evt)
                .Returns(Task.CompletedTask);

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
                new ChatContext(),
                new UserContext(),
                Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(),
                dbMock.Object,
                Mock.Of<IContactFormPluginLogService>());
        }

        private static async IAsyncEnumerable<StreamingChatMessageContent> EmptyStreamingContents()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static Kernel BuildKernel()
        {
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock
                .Setup(x => x.GetStreamingChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(EmptyStreamingContents());

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            return builder.Build();
        }

        // AuthorRole.User, matching LoadSiblingAgentContextBySessionQueryHandler.BuildInjectedMessageAsync
        // (not System — Anthropic rejects a mid-history 'system' role message; not Assistant — Anthropic
        // separately rejects an 'image' content block inside an assistant turn; see that handler's comment).
        private static ChatMessageContent BuildEphemeralInjectedMessage(string text) =>
            new(AuthorRole.User, text, metadata: new Dictionary<string, object>
            {
                { CommonMetadataConsts.IsEphemeralAgentContext, true },
                { CommonMetadataConsts.Timestamp, DateTimeOffset.UtcNow }
            });

        private static ChatMessageContent BuildRealPriorMessage(string text) =>
            new(AuthorRole.Assistant, text, metadata: new Dictionary<string, object>
            {
                { CommonMetadataConsts.Timestamp, DateTimeOffset.UtcNow.AddMinutes(-10) }
            });

        [Fact]
        public async Task RunChatCompletionAgentAsync_EphemeralSiblingContextInHistory_NeverReachesPersistedEvent()
        {
            var kernel = BuildKernel();
            var agent = new TenantAgent
            {
                Id = 1,
                Name = "MAISY",
                EnableSpinUpAgents = true,
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };

            var priorRealMessage = BuildRealPriorMessage("What did you edit last time?");
            var ephemeralMessage = BuildEphemeralInjectedMessage("[Context automatically shared by agent 'Vera']: I edited the logo for you");

            var history = new ChatHistory { priorRealMessage, ephemeralMessage };

            var data = new RunChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = [],
                Prompt = "You are MAISY.",
                Message = "thanks, what did Vera just do?",
                SessionId = "session-ephemeral",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(history),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = Guid.NewGuid()
            };

            await _service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.NotNull(_publishedEvent);

            // The ephemeral sibling context must never be part of what gets persisted...
            Assert.DoesNotContain(_publishedEvent.ChatHistory, m =>
                m.Metadata != null && m.Metadata.ContainsKey(CommonMetadataConsts.IsEphemeralAgentContext));
            Assert.DoesNotContain(_publishedEvent.ChatHistory, m => m.Content != null && m.Content.Contains("I edited the logo for you"));

            // ...while the root agent's own real prior turn and this turn's real new user message
            // (both genuinely part of its own transcript) must still be there.
            Assert.Contains(_publishedEvent.ChatHistory, m => m.Content == "What did you edit last time?");
            Assert.Contains(_publishedEvent.ChatHistory, m => m.Content == "thanks, what did Vera just do?");
        }

        [Fact]
        public async Task RunChatCompletionAgentAsync_EphemeralSiblingContextInHistory_IsStillVisibleToTheModelThisTurn()
        {
            // The whole point of the ephemeral injection: even though it must not be persisted, it must
            // still reach the model call for THIS turn.
            ChatHistory capturedModelHistory = null;
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock
                .Setup(x => x.GetStreamingChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ChatHistory, PromptExecutionSettings, Kernel, CancellationToken>((h, _, _, _) => capturedModelHistory = h)
                .Returns(EmptyStreamingContents());

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            var kernel = builder.Build();

            var agent = new TenantAgent
            {
                Id = 1,
                Name = "MAISY",
                EnableSpinUpAgents = true,
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };

            var ephemeralMessage = BuildEphemeralInjectedMessage("[Context automatically shared by agent 'Vera']: I edited the logo for you");
            var history = new ChatHistory { ephemeralMessage };

            var data = new RunChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = [],
                Prompt = "You are MAISY.",
                Message = "thanks, what did Vera just do?",
                SessionId = "session-ephemeral-2",
                ChatHistoryAgentThread = new ChatHistoryAgentThread(history),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Chat",
                UserMessageId = Guid.NewGuid()
            };

            await _service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.NotNull(capturedModelHistory);
            Assert.Contains(capturedModelHistory, m => m.Content != null && m.Content.Contains("Vera"));
        }
    }
}
