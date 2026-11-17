using AMAIS.WKFLO.AI.Application.Agents.Commands.TriggerMultiAgentOrchestrator;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.TriggerMultiAgentOrchestrator
{
    public class TriggerMultiAgentOrchestratorCommandHandlerTests
    {
        private readonly Mock<DaprClient> _daprClientMock;
        private readonly Mock<IMasterDataDbContext> _dbMock;
        private readonly TriggerMultiAgentOrchestratorCommandHandler _handler;

        public TriggerMultiAgentOrchestratorCommandHandlerTests()
        {
            _daprClientMock = new Mock<DaprClient>();
            _dbMock = new Mock<IMasterDataDbContext>();
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);
            _handler = new TriggerMultiAgentOrchestratorCommandHandler(
                _daprClientMock.Object,
                new TenantContext(),
                new ChatContext(),
                new UserContext(),
                new AgentSharableContext(),
                _dbMock.Object);
        }

        private static TriggerMultiAgentOrchestratorCommand BuildCommand(List<FileAttachment> files = null) => new()
        {
            Input = new AgentInvocationInput { Text = "kick off", Files = files },
            SessionId = "session-1",
            LeadAgent = new MultiAgentOrchestratorStep { AgentId = 1, AgentRole = "lead" }
        };

        [Fact]
        public async Task Handle_SmallPayload_SavesStateAndPublishesEvent()
        {
            var command = BuildCommand();

            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<bool>>(result);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>Bug 8189 / Task 8219: only the server-side hand-off may point an attachment at a stored document.</summary>
        [Fact]
        public async Task Handle_ClearsTheServerOnlyDocumentFields_BeforeQueueing()
        {
            WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand> queued = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, message, _, _, _) => queued = message)
                .Returns(Task.CompletedTask);
            var command = BuildCommand([new FileAttachment { FileName = "x.pdf", SourceDocumentId = System.Guid.NewGuid(), SourceDocument = new() }]);

            await _handler.Handle(command, TestContext.Current.CancellationToken);

            var file = Assert.Single(queued.Command.Input.Files);
            Assert.Null(file.SourceDocumentId);
            Assert.Null(file.SourceDocument);
        }

        [Fact]
        public async Task Handle_AttachmentsExceedMaxSize_ReturnsInvalidResultWithoutSavingState()
        {
            var oversizedFiles = new List<FileAttachment>
            {
                new() { FileName = "big.png", FileData = new string('a', 19_000_000) }
            };
            var command = BuildCommand(oversizedFiles);

            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.IsType<InvalidResult>(result);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _daprClientMock.Verify(x => x.PublishEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_LeadAgentHasWidgetChatSetting_ResolvesRootIsWidgetTrueOntoQueueMessageCommand()
        {
            // The lead agent IS the root of this orchestration run — its own widget/non-widget context
            // must be resolved once here (at trigger time) and threaded onto the queued command so
            // MultiAgentOrchestratorSubscriber/MultiAgentOrchestratorWorkflow can forward it to every
            // fan-out agent's AgentInvocationRequest.
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { Id = 1, AgentId = 1, IsWidget = true, ChatbotIconPath = "https://cdn.test/lead-widget-icon.png" }
            }.BuildMockDbSet().Object);

            WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand> captured = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, data, _, _, _) => captured = data)
                .Returns(Task.CompletedTask);

            var command = BuildCommand();

            await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.NotNull(captured);
            Assert.True(captured.Command.RootIsWidget);
        }

        [Fact]
        public async Task Handle_LeadAgentHasNoChatSetting_ResolvesRootIsWidgetNullOntoQueueMessageCommand()
        {
            // No AgentChatSettings row at all for the lead agent — must not throw, and RootIsWidget
            // must be left null (the downstream fallback in AgentChatIconHelper handles this).
            _dbMock.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand> captured = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, data, _, _, _) => captured = data)
                .Returns(Task.CompletedTask);

            var command = BuildCommand();

            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<bool>>(result);
            Assert.NotNull(captured);
            Assert.Null(captured.Command.RootIsWidget);
        }

        [Fact]
        public async Task Handle_SingleImageExceedsMaxSize_ReturnsInvalidResultWithoutSavingState()
        {
            // Under the aggregate MaxQueueMessageSizeBytes but over the per-image
            // MaxSingleImageSizeBytes (our stricter 5 MB business limit, well below
            // Anthropic's own 10 MB hard cap) — story #6670.
            var oversizedImage = new List<FileAttachment>
            {
                new() { FileName = "big.png", FileData = $"data:image/png;base64,{new string('A', 14_000_000)}" }
            };
            var command = BuildCommand(oversizedImage);

            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            Assert.IsType<InvalidResult>(result);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
