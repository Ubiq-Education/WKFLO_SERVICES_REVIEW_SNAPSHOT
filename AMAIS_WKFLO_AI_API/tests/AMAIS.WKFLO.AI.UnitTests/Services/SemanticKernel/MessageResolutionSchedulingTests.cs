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
    /// US 7142: <c>MessageResolutionWorkflow</c> must be scheduled right where <c>ConversationAnalysisWorkflow</c>
    /// already is, with the exact same "never break the chat response" guarantee — a failed/absent scheduler
    /// must not surface as an exception from <c>RunChatCompletionAgentAsync</c>.
    /// </summary>
    public class MessageResolutionSchedulingTests
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

        private static SemanticKernelAgentService BuildService(IMessageResolutionWorkflowScheduler scheduler, out TenantContext tenantContext, out UserContext userContext)
        {
            var dbMock = new Mock<IMasterDataDbContext>();
            dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            dbMock.Setup(x => x.AgentAllowedSpinUpAgents).Returns(new List<AgentAllowedSpinUpAgent>().BuildMockDbSet().Object);

            tenantContext = new TenantContext { TenantName = "acme" };
            userContext = new UserContext { Guid = "user-1" };

            return new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(),
                Mock.Of<IWkfloMapper>(),
                Mock.Of<IDatastreamProvider>(),
                Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(),
                Mock.Of<IMcpClientService>(),
                Mock.Of<IWkfloMediator>(),
                Options.Create(new ElasticsearchSettings()),
                tenantContext,
                new ChatContext(),
                userContext,
                Mock.Of<IBlobStorageProvider>(),
                Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(),
                dbMock.Object,
                Mock.Of<IContactFormPluginLogService>(),
                agentSharableContext: null,
                messageResolutionWorkflowScheduler: scheduler);
        }

        private static RunChatData BuildRunChatData(Kernel kernel, string message, Guid userMessageId, int agentId = 7, string sessionId = "session-1") => new()
        {
            Kernel = kernel,
            Agent = new TenantAgent { Id = agentId, Name = "Support Bot", AIModel = new AIModel { Provider = "openai", Model = "gpt-4o" } },
            AgentTagNames = [],
            Prompt = "You are a helpful assistant.",
            Message = message,
            SessionId = sessionId,
            ChatHistoryAgentThread = new ChatHistoryAgentThread(new ChatHistory()),
            CreatedAt = DateTimeOffset.UtcNow,
            ChatName = "Test Chat",
            UserMessageId = userMessageId
        };

        [Fact]
        public async Task RunChatCompletionAgentAsync_SchedulesMessageResolutionWorkflow_WithQuestionAnswerAndMessageId()
        {
            var schedulerMock = new Mock<IMessageResolutionWorkflowScheduler>();
            MessageResolutionWorkflowInput scheduledInput = null;
            schedulerMock
                .Setup(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()))
                .Callback<string, MessageResolutionWorkflowInput>((_, input) => scheduledInput = input)
                .Returns(Task.CompletedTask);

            var service = BuildService(schedulerMock.Object, out _, out _);
            var kernel = BuildKernelReturning("Sure, here's how to reset your password.");
            var userMessageId = Guid.NewGuid();
            var data = BuildRunChatData(kernel, "How do I reset my password?", userMessageId);

            await service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            schedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()), Times.Once);
            Assert.NotNull(scheduledInput);
            Assert.Equal("acme", scheduledInput.TenantName);
            Assert.Equal(7, scheduledInput.AgentId);
            Assert.Equal("session-1", scheduledInput.SessionId);
            Assert.Equal("user-1", scheduledInput.UserId);
            Assert.Equal(userMessageId, scheduledInput.MessageId);
            Assert.Equal("How do I reset my password?", scheduledInput.Question);
            Assert.Equal("Sure, here's how to reset your password.", scheduledInput.Answer);
        }

        [Fact]
        public async Task RunChatCompletionAgentAsync_SchedulerThrows_DoesNotPropagate_AndStillReturnsAnswer()
        {
            var schedulerMock = new Mock<IMessageResolutionWorkflowScheduler>();
            schedulerMock
                .Setup(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()))
                .ThrowsAsync(new InvalidOperationException("workflow engine unreachable"));

            var service = BuildService(schedulerMock.Object, out _, out _);
            var kernel = BuildKernelReturning("Here is the answer.");
            var data = BuildRunChatData(kernel, "A question", Guid.NewGuid());

            var result = await service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal("Here is the answer.", result.Message);
        }

        [Fact]
        public async Task RunChatCompletionAgentAsync_BlankAssistantResponse_DoesNotScheduleMessageResolutionWorkflow()
        {
            // A turn that produced no real answer text (e.g. the streaming-failure path clearing
            // assistantResponseList) must never schedule a resolution judgment - judging a blank exchange
            // would fabricate an IsResolved verdict off a non-answer, the same rule already enforced for the
            // ContactForm filler case.
            var schedulerMock = new Mock<IMessageResolutionWorkflowScheduler>();
            var service = BuildService(schedulerMock.Object, out _, out _);
            var kernel = BuildKernelReturning("");
            var data = BuildRunChatData(kernel, "A question", Guid.NewGuid());

            await service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            schedulerMock.Verify(x => x.ScheduleAsync(It.IsAny<string>(), It.IsAny<MessageResolutionWorkflowInput>()), Times.Never);
        }

        [Fact]
        public async Task RunChatCompletionAgentAsync_NoSchedulerConfigured_DoesNotThrow()
        {
            // The trailing constructor parameter defaults to null (mirrors AgentSharableContext) — the
            // service must simply skip scheduling, not throw a NullReferenceException.
            var service = BuildService(scheduler: null, out _, out _);
            var kernel = BuildKernelReturning("Here is the answer.");
            var data = BuildRunChatData(kernel, "A question", Guid.NewGuid());

            var result = await service.RunChatCompletionAgentAsync(data, TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal("Here is the answer.", result.Message);
        }
    }
}
