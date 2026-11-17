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
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// US 7142 follow-up (Copilot PR review finding, confirmed by the developer): the voice run path
    /// (microphone) never called <c>RunChatClassificationAsync</c> at all, so voice-triggered messages had
    /// no <c>MessageTopicClassification</c> row and were permanently invisible to Topic Detail analytics.
    /// <see cref="SemanticKernelAgentService.RunVoiceChatCompletionAgentAsync"/> now schedules
    /// <c>MessageResolutionWorkflow</c> the same way <c>RunChatCompletionAgentAsync</c> (text) already does -
    /// these tests mirror <see cref="AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel.MessageResolutionSchedulingTests"/>'s
    /// own coverage for the voice path.
    /// </summary>
    public class RunVoiceMessageResolutionSchedulingTests
    {
        private static async IAsyncEnumerable<StreamingChatMessageContent> StreamingContents(params string[] chunks)
        {
            await Task.CompletedTask;
            foreach (var chunk in chunks)
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk);
        }

        private static Kernel BuildKernelReturning(params string[] chunks)
        {
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            chatCompletionServiceMock
                .Setup(x => x.GetStreamingChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(StreamingContents(chunks));

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            return builder.Build();
        }

        private static SemanticKernelAgentService BuildService(IMessageResolutionWorkflowScheduler scheduler)
        {
            var dbMock = new Mock<IMasterDataDbContext>();
            dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            return new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(),
                Mock.Of<IWkfloMapper>(),
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(),
                Mock.Of<IMcpClientService>(),
                Mock.Of<IWkfloMediator>(),
                Options.Create(new ElasticsearchSettings()),
                new TenantContext { TenantName = "acme" },
                new ChatContext(),
                new UserContext { Guid = "user-1" },
                Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(),
                dbMock.Object,
                Mock.Of<IContactFormPluginLogService>(),
                agentSharableContext: null,
                messageResolutionWorkflowScheduler: scheduler);
        }

        private static RunVoiceChatData BuildData(Kernel kernel, Guid userMessageId, string sessionId = "session-voice-1") => new()
        {
            Kernel = kernel,
            Agent = new TenantAgent { Id = 9, Name = "Voice Agent", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
            AgentTagNames = [],
            Prompt = "You are a helpful assistant.",
            Message = "How do I reset my password?",
            SessionId = sessionId,
            ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
            CreatedAt = DateTimeOffset.UtcNow,
            ChatName = "Test Voice Chat",
            Voice = "en-US-JennyNeural",
            Language = "en-US",
            UserMessageId = userMessageId
        };

        [Fact]
        public async Task RunVoiceChatCompletionAgentAsync_SchedulesMessageResolutionWorkflow_WithQuestionAnswerAndMessageId()
        {
            var schedulerMock = new Mock<IMessageResolutionWorkflowScheduler>();
            MessageResolutionWorkflowInput scheduledInput = null;
            schedulerMock
                .Setup(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()))
                .Callback<string, MessageResolutionWorkflowInput>((_, input) => scheduledInput = input)
                .Returns(Task.CompletedTask);

            var service = BuildService(schedulerMock.Object);
            var kernel = BuildKernelReturning("Sure, here's how to reset your password.");
            var userMessageId = Guid.NewGuid();
            var data = BuildData(kernel, userMessageId);

            await service.RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            schedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()), Times.Once);
            Assert.NotNull(scheduledInput);
            Assert.Equal("acme", scheduledInput.TenantName);
            Assert.Equal(9, scheduledInput.AgentId);
            Assert.Equal("session-voice-1", scheduledInput.SessionId);
            Assert.Equal("user-1", scheduledInput.UserId);
            Assert.Equal(userMessageId, scheduledInput.MessageId);
            Assert.Equal("How do I reset my password?", scheduledInput.Question);
            Assert.Equal("Sure, here's how to reset your password.", scheduledInput.Answer);
        }

        [Fact]
        public async Task RunVoiceChatCompletionAgentAsync_BlankAssistantResponse_DoesNotScheduleMessageResolutionWorkflow()
        {
            // Same "never fabricate a resolution off a non-answer" rule as the text path - a turn with no
            // real answer text must never schedule a resolution judgment.
            var schedulerMock = new Mock<IMessageResolutionWorkflowScheduler>();
            var service = BuildService(schedulerMock.Object);
            var kernel = BuildKernelReturning("");
            var data = BuildData(kernel, Guid.NewGuid());

            await service.RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            schedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()), Times.Never);
        }

        [Fact]
        public async Task RunVoiceChatCompletionAgentAsync_NoSchedulerConfigured_DoesNotThrow()
        {
            var service = BuildService(scheduler: null);
            var kernel = BuildKernelReturning("Here is the answer.");
            var data = BuildData(kernel, Guid.NewGuid());

            var result = await service.RunVoiceChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal("Here is the answer.", result.Message);
        }
    }
}
