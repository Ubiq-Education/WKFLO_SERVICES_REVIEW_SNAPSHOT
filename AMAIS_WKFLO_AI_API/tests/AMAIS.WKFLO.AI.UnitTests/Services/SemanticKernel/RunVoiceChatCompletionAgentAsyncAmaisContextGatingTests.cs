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

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Covers Task 7015: the Amais CMS page/feature integration context line (built by
    /// SemanticKernelAgentService.BuildAmaisContextLine) must only be spliced into the prompt sent to the
    /// chat completion service when RunVoiceChatData.AmaisSkillsEnabled is true. Previously the context line
    /// was spliced in unconditionally, so a voice agent with Amais skills disabled still received
    /// instructions telling it to call Amais-only tools.
    /// </summary>
    public class RunVoiceChatCompletionAgentAsyncAmaisContextGatingTests
    {
        private const string AmaisContextMarker = "CRITICAL CONTEXT - READ FIRST:";
        private const string CapabilitiesMarker = "YOUR AVAILABLE TOOLS/PLUGINS:";

        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly ChatContext _chatContext;
        private readonly SemanticKernelAgentService _service;

        public RunVoiceChatCompletionAgentAsyncAmaisContextGatingTests()
        {
            _notificationServiceMock = new Mock<IChatNotificationService>();

            _dbMock = new Mock<IMasterDataDbContext>();
            var agentChatSettingsDbSet = new List<AgentChatSetting>().BuildMockDbSet();
            _dbMock.Setup(x => x.AgentChatSettings).Returns(agentChatSettingsDbSet.Object);

            // PagePath/FeatureId are set so BuildAmaisContextLine (if invoked) produces a non-trivial
            // context line, including the "CRITICAL - FEATURE DATA HANDLING:" feature block.
            _chatContext = new ChatContext
            {
                PagePath = "/pricing",
                FeatureId = "feature-123",
                FeatureType = "Text",
                FeatureName = "Hero Banner"
            };

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
                _chatContext,
                new UserContext(),
                Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(),
                _dbMock.Object,
                Mock.Of<IContactFormPluginLogService>());
        }

        private static async IAsyncEnumerable<StreamingChatMessageContent> EmptyStreamingContents()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static Kernel BuildKernelCapturingChatHistory(out Func<ChatHistory> getCapturedHistory)
        {
            ChatHistory capturedHistory = null;

            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock
                .Setup(x => x.GetStreamingChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ChatHistory, PromptExecutionSettings, Kernel, CancellationToken>(
                    (history, _, _, _) => capturedHistory = history)
                .Returns(EmptyStreamingContents());

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);

            getCapturedHistory = () => capturedHistory;
            return builder.Build();
        }

        private static bool CapturedHistoryContains(ChatHistory history, string marker)
        {
            if (history == null)
                return false;

            foreach (var message in history)
            {
                if (message.Content != null && message.Content.Contains(marker))
                    return true;
            }

            return false;
        }

        private static TenantAgent BuildAgentWithTool()
        {
            var agent = new TenantAgent
            {
                Id = 100,
                Name = "Voice Gating Agent",
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };
            agent.AgentTools =
            [
                new AgentTool
                {
                    AgentId = agent.Id,
                    Tool = new TenantTool { Id = 1, Name = "Contact Form", Description = "Collects visitor contact details", IsActive = true }
                }
            ];
            return agent;
        }

        private RunVoiceChatData BuildRunVoiceChatData(Kernel kernel, bool amaisSkillsEnabled, string sessionId, RunnableAgent agent = null)
        {
            agent ??= new TenantAgent
            {
                Id = 100,
                Name = "Voice Gating Agent",
                AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" }
            };

            return new RunVoiceChatData
            {
                Kernel = kernel,
                Agent = agent,
                AgentTagNames = new List<string>(),
                Prompt = "You are a helpful assistant.",
                Message = "hello",
                SessionId = sessionId,
                ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
                CreatedAt = DateTimeOffset.UtcNow,
                ChatName = "Test Voice Chat",
                Voice = "en-US-JennyNeural",
                Language = "en-US",
                AmaisSkillsEnabled = amaisSkillsEnabled
            };
        }

        [Fact]
        public async Task RunVoiceChatCompletionAgentAsync_WhenAmaisSkillsEnabled_SplicesAmaisContextLineIntoPrompt()
        {
            var kernel = BuildKernelCapturingChatHistory(out var getCapturedHistory);
            var data = BuildRunVoiceChatData(kernel, amaisSkillsEnabled: true, sessionId: "session-voice-amais-on");

            await _service.RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.True(CapturedHistoryContains(getCapturedHistory(), AmaisContextMarker));
        }

        [Fact]
        public async Task RunVoiceChatCompletionAgentAsync_WhenAmaisSkillsDisabled_OmitsAmaisContextLineFromPrompt()
        {
            var kernel = BuildKernelCapturingChatHistory(out var getCapturedHistory);
            var data = BuildRunVoiceChatData(kernel, amaisSkillsEnabled: false, sessionId: "session-voice-amais-off");

            await _service.RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.False(CapturedHistoryContains(getCapturedHistory(), AmaisContextMarker));
        }

        /// <summary>
        /// Covers Task 6985 follow-up: BuildAmaisAgentCapabilitiesInjection is only invoked when
        /// AmaisSkillsEnabled is true (same gate as the Amais context line above) — a voice agent with
        /// tools configured but Amais skills disabled must not receive the capabilities list either.
        /// </summary>
        [Fact]
        public async Task RunVoiceChatCompletionAgentAsync_WhenAmaisSkillsEnabled_SplicesCapabilitiesInjectionIntoPrompt()
        {
            var kernel = BuildKernelCapturingChatHistory(out var getCapturedHistory);
            var data = BuildRunVoiceChatData(kernel, amaisSkillsEnabled: true, sessionId: "session-voice-capabilities-on", agent: BuildAgentWithTool());

            await _service.RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.True(CapturedHistoryContains(getCapturedHistory(), CapabilitiesMarker));
        }

        [Fact]
        public async Task RunVoiceChatCompletionAgentAsync_WhenAmaisSkillsDisabled_OmitsCapabilitiesInjectionFromPrompt()
        {
            var kernel = BuildKernelCapturingChatHistory(out var getCapturedHistory);
            var data = BuildRunVoiceChatData(kernel, amaisSkillsEnabled: false, sessionId: "session-voice-capabilities-off", agent: BuildAgentWithTool());

            await _service.RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.False(CapturedHistoryContains(getCapturedHistory(), CapabilitiesMarker));
        }
    }
}
