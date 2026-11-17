using AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.EndPoints.Public.Agents
{
    public class PublicAgentChatMessageTests
    {
        private readonly Mock<DaprClient> _daprClientMock;
        private readonly PublicAgentChatMessage _endpoint;

        private static readonly DateTimeOffset QueuedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        public PublicAgentChatMessageTests()
        {
            _daprClientMock = new Mock<DaprClient>();
            _endpoint = new PublicAgentChatMessage(
                Mock.Of<IWkfloMediator>(),
                _daprClientMock.Object,
                new TenantContext(),
                new ChatContext(),
                new UserContext(),
                new AgentSharableContext(),
                Mock.Of<IDateTimeProvider>(x => x.DateTimeOffsetUtcNow() == QueuedAt),
                Mock.Of<AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop.IChatSessionOwnerStore>());
        }

        private static MessagePublicAgentByShareIdCommand BuildCommand(List<FileAttachment> files = null) => new()
        {
            Input = new MessagePublicAgentInputCommand { Text = "hello", Files = files },
            SessionId = "session-1"
        };

        [Fact]
        public async Task HandleAsync_SmallPayload_SavesStateAndReturnsSuccess()
        {
            var result = await _endpoint.HandleAsync("share-1", BuildCommand(), TestContext.Current.CancellationToken);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.IsType<SuccessResult<bool>>(okResult.Value);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<MessagePublicAgentByShareIdCommand>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>Bug 8189 / Task 8219: a request body cannot point an attachment at a stored document; only the server-side hand-off can.</summary>
        [Fact]
        public async Task HandleAsync_ClearsTheServerOnlyDocumentFields_BeforeQueueing()
        {
            WkfloQueueMessage<MessagePublicAgentByShareIdCommand> queued = null;
            _daprClientMock
                .Setup(x => x.SaveStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<MessagePublicAgentByShareIdCommand>>(),
                    It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, WkfloQueueMessage<MessagePublicAgentByShareIdCommand>, StateOptions, IReadOnlyDictionary<string, string>, CancellationToken>(
                    (_, _, message, _, _, _) => queued = message)
                .Returns(Task.CompletedTask);
            var files = new List<FileAttachment>
            {
                new() { FileName = "x.pdf", SourceDocumentId = Guid.NewGuid(), SourceDocument = new AMAIS.WKFLO.Common.AI.Application.Services.Documents.AttachedDocumentInfo { DocumentId = Guid.NewGuid() } }
            };

            await _endpoint.HandleAsync("share-1", BuildCommand(files), TestContext.Current.CancellationToken);

            var file = Assert.Single(queued.Command.Input.Files);
            Assert.Null(file.SourceDocumentId);
            Assert.Null(file.SourceDocument);
        }

        [Fact]
        public async Task HandleAsync_ImageExceedsMaxSize_ReturnsBadRequestWithoutSavingState()
        {
            var oversizedImage = new List<FileAttachment>
            {
                new() { FileName = "big.png", FileData = $"data:image/png;base64,{new string('A', 8_000_000)}" }
            };

            var result = await _endpoint.HandleAsync("share-1", BuildCommand(oversizedImage), TestContext.Current.CancellationToken);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.IsType<InvalidResult>(badRequest.Value);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<MessagePublicAgentByShareIdCommand>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleAsync_UnsupportedImageFormat_ReturnsBadRequestWithoutSavingState()
        {
            var heicBytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63 };
            var heicFile = new List<FileAttachment>
            {
                new() { FileName = "photo.heic", FileData = $"data:image/heic;base64,{System.Convert.ToBase64String(heicBytes)}" }
            };

            var result = await _endpoint.HandleAsync("share-1", BuildCommand(heicFile), TestContext.Current.CancellationToken);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.IsType<InvalidResult>(badRequest.Value);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<MessagePublicAgentByShareIdCommand>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleAsync_AttachmentsExceedAggregateMaxSize_ReturnsBadRequestWithoutSavingState()
        {
            var oversizedFiles = new List<FileAttachment>
            {
                new() { FileName = "big.bin", FileData = new string('a', 19_000_000) }
            };

            var result = await _endpoint.HandleAsync("share-1", BuildCommand(oversizedFiles), TestContext.Current.CancellationToken);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.IsType<InvalidResult>(badRequest.Value);
            _daprClientMock.Verify(x => x.SaveStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WkfloQueueMessage<MessagePublicAgentByShareIdCommand>>(),
                It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
