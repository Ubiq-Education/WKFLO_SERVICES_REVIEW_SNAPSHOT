using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration;
using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Flurl.Http;
using Moq;
using static AMAIS.WKFLO.Common.Models.CommonConsts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration
{
    public class ImageGenerationPluginTests
    {
        private readonly Mock<IImageEditService> _imageEditService;
        private readonly Mock<IChatNotificationService> _notificationService;
        private readonly Mock<IBlobStorageProvider> _blobStorageProvider;
        private readonly Mock<IWkfloMediator> _mediator;
        private readonly Mock<ILogger> _logger;
        private readonly ChatContext _chatContext;
        private readonly TenantContext _tenantContext = new() { TenantGuid = TenantGuid };

        private const string SessionId = "test-session-id";
        private const string TenantGuid = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        private const string ToolName = "ImageEditTool";
        private const string BlobUrl = "https://storage.example.com/chat-images/edited/image.png";
        private const string SasUrl = "https://storage.example.com/chat-images/edited/image.png?sig=abc123";
        private const string ImageUrl = "https://storage.example.com/session/photo.jpg";
        private const string Instruction = "Make it black and white";

        public ImageGenerationPluginTests()
        {
            _imageEditService = new Mock<IImageEditService>();
            _notificationService = new Mock<IChatNotificationService>();
            _blobStorageProvider = new Mock<IBlobStorageProvider>();
            _mediator = new Mock<IWkfloMediator>();
            _logger = new Mock<ILogger>();

            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, items: new ChatMessageContentItemCollection
            {
                new ImageContent(new Uri(ImageUrl))
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
        public async Task EditImageAsync_HappyPath_ReturnsImgTagWithSasUrl()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(ImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1, 2, 3 }, "image/png", "png", new AITokenUsage()));

            var result = await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing completed successfully.", result);
            // Bug 8289: without this ask the model often ends the turn with an empty "<p></p>" reply.
            Assert.Contains("Reply with one short sentence", result);
        }

        [Fact]
        public async Task EditImageAsync_HappyPath_UploadsToChatImagesContainer()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(ImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1, 2, 3 }, "image/png", "png", new AITokenUsage()));

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            _blobStorageProvider.Verify(x => x.UploadAsync(
                "chat-images",
                It.Is<string>(n => n.StartsWith(TenantGuid + "/edited/") && n.EndsWith(".png")),
                It.IsAny<Stream>(),
                "image/png",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EditImageAsync_TenantPrefixedBlob_FileNameShownToTheChatStaysTenantLess()
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

            _imageEditService
                .Setup(x => x.EditImageAsync(ImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1, 2, 3 }, "image/png", "png", new AITokenUsage()));

            string uploadedBlobName = null;
            _blobStorageProvider
                .Setup(x => x.UploadAsync("chat-images", It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, Stream, string, CancellationToken>((_, name, _, _, _) => uploadedBlobName = name)
                .ReturnsAsync(BlobUrl);

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            var message = Assert.Single(_chatContext.AssistantPostMessagesToBeProcessed);
            var fileName = message.Metadata[CommonMetadataConsts.FileName].ToString();
            Assert.Equal($"{TenantGuid}/{fileName}", uploadedBlobName);
            Assert.StartsWith("edited/", fileName);
            Assert.DoesNotContain(TenantGuid, message.Items.OfType<TextContent>().Single().Text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("not-a-guid")]
        public async Task EditImageAsync_UnknownTenant_UsesLegacyEditedBlobName(string tenantGuid)
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

            _imageEditService
                .Setup(x => x.EditImageAsync(ImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1, 2, 3 }, "image/png", "png", new AITokenUsage()));

            var result = await BuildPlugin(dateTimeProviderMock, tenantContext: new TenantContext { TenantGuid = tenantGuid })
                .EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing completed successfully.", result);
            _blobStorageProvider.Verify(x => x.UploadAsync(
                "chat-images",
                It.Is<string>(n => n.StartsWith("edited/")),
                It.IsAny<Stream>(),
                "image/png",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EditImageAsync_HappyPath_PublishesTokenUsageEvent()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tokenUsage = new AITokenUsage { InputTokenCount = 250, OutputTokenCount = 0 };
            _imageEditService
                .Setup(x => x.EditImageAsync(ImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1 }, "image/png", "png", tokenUsage));

            await BuildPlugin(dateTimeProviderMock, provider: "azure", model: "gpt-image-2")
                .EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            _mediator.Verify(x => x.Publish(
                It.Is<AgentTokensUsedDomainEvent>(e =>
                    e.AgentAnswer.TokenUsage.InputTokenCount == 250 &&
                    e.AgentAnswer.Provider == "azure" &&
                    e.AgentAnswer.Model == "gpt-image-2"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EditImageAsync_HappyPath_SendsRunningThenCompletedNotifications()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(ImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1 }, "image/png", "png", new AITokenUsage()));

            var statuses = new List<string>();
            _notificationService
                .Setup(x => x.SendToolExecutionUpdate(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()))
                .Callback<string, string, string, string, int?, string>((_, _, status, _, _, _) => statuses.Add(status))
                .Returns(Task.CompletedTask);

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(new[] { "running", "completed" }, statuses);
        }

        [Fact]
        public async Task EditImageAsync_QualityAndAspectRatioProvided_PassesOverridesToService()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(ImageUrl, Instruction, "low", "16:9", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1, 2, 3 }, "image/png", "png", new AITokenUsage()));

            var result = await BuildPlugin(dateTimeProviderMock)
                .EditImageAsync(Instruction, quality: "low", aspectRatio: "16:9", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing completed successfully.", result);
            _imageEditService.Verify(x => x.EditImageAsync(ImageUrl, Instruction, "low", "16:9", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EditImageAsync_QualityAndAspectRatioOmitted_PassesNullOverridesToService()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(ImageUrl, Instruction, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1, 2, 3 }, "image/png", "png", new AITokenUsage()));

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            _imageEditService.Verify(x => x.EditImageAsync(ImageUrl, Instruction, null, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EditImageAsync_ServiceThrows_ReturnsErrorHtml()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("API unavailable"));

            var result = await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("<p", result);
            Assert.Contains("Image editing failed", result);
        }

        [Fact]
        public async Task EditImageAsync_ServiceThrows_SendsFailureNotification()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("API unavailable"));

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            _notificationService.Verify(x => x.SendToolExecutionUpdate(
                SessionId, ToolName, "failed",
                It.IsAny<string>(), 500, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task EditImageAsync_ServiceThrows_DoesNotPublishTokenEvent()
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("failure"));

            await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            _mediator.Verify(
                x => x.Publish(It.IsAny<AgentTokensUsedDomainEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Theory]
        [InlineData(ImageEditFailureKind.SafetyBlocked, "safety filters", LogLevel.Warning)]
        [InlineData(ImageEditFailureKind.Overloaded, "very busy", LogLevel.Warning)]
        [InlineData(ImageEditFailureKind.RateLimited, "Too many image requests", LogLevel.Warning)]
        [InlineData(ImageEditFailureKind.QuotaExceeded, "usage limit", LogLevel.Error)]
        [InlineData(ImageEditFailureKind.InvalidRequest, "couldn't process", LogLevel.Warning)]
        [InlineData(ImageEditFailureKind.NotConfigured, "isn't set up correctly", LogLevel.Error)]
        [InlineData(ImageEditFailureKind.Other, "Something went wrong", LogLevel.Error)]
        public async Task EditImageAsync_ProviderFailure_ReturnsFriendlyTextHidesRawDetailsAndLogsOnce(ImageEditFailureKind kind, string expectedText, LogLevel expectedLevel)
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ImageEditProviderException(kind, "google", "RESOURCE_EXHAUSTED FinishReason=IMAGE_SAFETY raw", "SAFETY", new[] { "IMAGE_SAFETY" }));

            var result = await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.StartsWith("<p>Image editing failed: ", result);
            Assert.Contains(expectedText, result);
            Assert.Contains("Explain this to the user in your own words", result);
            Assert.DoesNotContain("RESOURCE_EXHAUSTED", result);
            Assert.DoesNotContain("FinishReason=", result);
            Assert.DoesNotContain("administrator", result);

            _logger.Verify(x => x.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            _logger.Verify(x => x.Log(
                expectedLevel, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);

            _notificationService.Verify(x => x.SendToolExecutionUpdate(
                SessionId, ToolName, "failed",
                It.Is<string>(d => d.StartsWith("Image editing failed:") && !d.Contains("RESOURCE_EXHAUSTED")), 500, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task EditImageAsync_GoogleApiException_IsClassifiedAndRawStatusHidden()
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Google.GenAI.ApiException("The model is overloaded", 503, "UNAVAILABLE", null));

            var result = await BuildPlugin(dateTimeProviderMock).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("very busy", result);
            Assert.DoesNotContain("UNAVAILABLE", result);
            Assert.DoesNotContain("overloaded", result);
        }

        [Fact]
        public async Task EditImageAsync_NoImageInConversation_KeepsActionableMessage()
        {
            _chatContext.CurrentChatHistory = new ChatHistory();

            var result = await BuildPlugin(new Mock<IDateTimeProvider>()).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Please upload an image first.", result);
        }

        [Fact]
        public async Task EditImageAsync_CallerCancelled_RethrowsWithoutErrorLog()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException(cts.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                BuildPlugin(new Mock<IDateTimeProvider>()).EditImageAsync(Instruction, cancellationToken: cts.Token));

            _logger.Verify(x => x.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
        }

        [Fact]
        public async Task EditImageAsync_CallerCancelledAndFlurlWrapsIt_RethrowsWithoutErrorLogOrFailedUpdate()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            using var httpTest = new Flurl.Http.Testing.HttpTest();
            Flurl.Http.FlurlHttpException wrapped = null;
            try
            {
                httpTest.SimulateTimeout();
                await "https://azure.example.com/images/edits".PostStringAsync("x", cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (Flurl.Http.FlurlHttpException ex)
            {
                wrapped = ex;
            }

            Assert.NotNull(wrapped);
            _imageEditService
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(wrapped);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                BuildPlugin(new Mock<IDateTimeProvider>()).EditImageAsync(Instruction, cancellationToken: cts.Token));

            _logger.Verify(x => x.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
            _notificationService.Verify(x => x.SendToolExecutionUpdate(
                SessionId, ToolName, "failed", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
        }

        private ImageGenerationPlugin BuildPlugin(Mock<IDateTimeProvider> _dateTimeProviderMock, string provider = "azure", string model = "gpt-image-2", TenantContext tenantContext = null) =>
            new(
                new TenantTool { Id = 42, Name = ToolName, Type = "Image_Generation", Config = "{}" },
                _imageEditService.Object,
                _notificationService.Object,
                _chatContext,
                tenantContext ?? _tenantContext,
                _blobStorageProvider.Object,
                _mediator.Object,
                _dateTimeProviderMock.Object,
                _logger.Object,
                provider,
                model,
                "agentType");
    }
}
