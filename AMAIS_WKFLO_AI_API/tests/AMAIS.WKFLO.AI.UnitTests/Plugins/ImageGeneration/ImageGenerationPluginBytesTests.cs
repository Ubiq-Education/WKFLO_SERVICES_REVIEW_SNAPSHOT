using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration
{
    public class ImageGenerationPluginBytesTests
    {
        private readonly Mock<IImageEditService> _imageEditService;
        private readonly Mock<IChatNotificationService> _notificationService;
        private readonly Mock<IBlobStorageProvider> _blobStorageProvider;
        private readonly Mock<IWkfloMediator> _mediator;
        private readonly Mock<ILogger> _logger;
        private readonly ChatContext _chatContext;

        private const string SessionId = "bytes-session-id";
        private const string ToolName = "BytesImageTool";
        private const string BlobUrl = "https://storage.example.com/chat-images/edited/result.png";
        private const string SasUrl = "https://storage.example.com/chat-images/edited/result.png?sig=xyz";
        private const string Instruction = "Convert to oil painting style";

        private static readonly byte[] FakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };

        public ImageGenerationPluginBytesTests()
        {
            _imageEditService = new Mock<IImageEditService>();
            _notificationService = new Mock<IChatNotificationService>();
            _blobStorageProvider = new Mock<IBlobStorageProvider>();
            _mediator = new Mock<IWkfloMediator>();
            _logger = new Mock<ILogger>();

            // Chat history with bytes-only ImageContent (no Uri) — exercises the bytes fallback path
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, items: new ChatMessageContentItemCollection
            {
                new ImageContent(new System.ReadOnlyMemory<byte>(FakeImageBytes), "image/png")
            }));

            _chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = chatHistory };

            _notificationService
                .Setup(x => x.SendToolExecutionUpdate(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            _blobStorageProvider
                .Setup(x => x.UploadAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(BlobUrl);

            _blobStorageProvider
                .Setup(x => x.GenerateSasUrlAsync(It.IsAny<Uri>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(SasUrl);

            _mediator
                .Setup(x => x.Publish(It.IsAny<AgentTokensUsedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task EditImageAsync_BytesOnlyInHistory_PassesBytesToService()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            byte[] capturedBytes = null;
            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<byte[]>(), Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<byte[], string, string, string, CancellationToken>((b, _, _, _, _) => capturedBytes = b)
                .ReturnsAsync(new ImageEditResult(new byte[] { 1 }, "image/png", "png", new AITokenUsage()));

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(capturedBytes);
            Assert.Equal(FakeImageBytes, capturedBytes);
        }

        [Fact]
        public async Task EditImageAsync_BytesOnlyInHistory_NeverCallsUrlOverload()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<byte[]>(), Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1 }, "image/png", "png", new AITokenUsage()));

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            _imageEditService.Verify(
                x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task EditImageAsync_TokenCountsFromBytesOverloadAreReported()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tokenUsage = new AITokenUsage { InputTokenCount = 500, OutputTokenCount = 150 };
            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<byte[]>(), Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1 }, "image/png", "png", tokenUsage));

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            _mediator.Verify(x => x.Publish(
                It.Is<AgentTokensUsedDomainEvent>(e =>
                    e.AgentAnswer.TokenUsage.InputTokenCount == 500 &&
                    e.AgentAnswer.TokenUsage.OutputTokenCount == 150),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EditImageAsync_ServiceThrowsOnBytesPath_ReturnsErrorHtml()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Bytes processing failed"));

            var result = await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing failed", result);
        }

        private ImageGenerationPlugin BuildPlugin(Mock<IDateTimeProvider> _dateTimeProviderMock) =>
            new(
                new TenantTool { Id = 99, Name = ToolName, Type = "Image_Generation", Config = "{}" },
                _imageEditService.Object,
                _notificationService.Object,
                _chatContext,
                new TenantContext { TenantGuid = Guid.NewGuid().ToString() },
                _blobStorageProvider.Object,
                _mediator.Object,
                _dateTimeProviderMock.Object,
                _logger.Object,
                "google",
                "gemini-2.0-flash",
                "agentType");
    }
}
