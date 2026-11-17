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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration
{
    public class ImageGenerationPluginChatHistoryTests
    {
        private const string SessionId = "history-session-id";
        private const string ToolName = "HistoryImageTool";
        private const string FirstImageUrl = "https://storage.example.com/session/first.jpg";
        private const string LastImageUrl = "https://storage.example.com/session/last.jpg";
        private const string BlobUrl = "https://storage.example.com/chat-images/edited/out.png";
        private const string SasUrl = "https://storage.example.com/chat-images/edited/out.png?sig=abc";
        private const string Instruction = "Add a watermark";

        private static readonly byte[] FakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        private ImageGenerationPlugin BuildPlugin(ChatContext chatContext, Mock<IImageEditService> imageEditService)
        {
            // Setup mocked DateTimeProvider
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var notificationService = new Mock<IChatNotificationService>();
            notificationService
                .Setup(x => x.SendToolExecutionUpdate(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var blobStorageProvider = new Mock<IBlobStorageProvider>();
            blobStorageProvider
                .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(BlobUrl);
            blobStorageProvider
                .Setup(x => x.GenerateSasUrlAsync(It.IsAny<Uri>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(SasUrl);

            var mediator = new Mock<IWkfloMediator>();
            mediator.Setup(x => x.Publish(It.IsAny<AgentTokensUsedDomainEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            return new ImageGenerationPlugin(
                new TenantTool { Id = 1, Name = ToolName, Type = "Image_Generation", Config = "{}" },
                imageEditService.Object,
                notificationService.Object,
                chatContext,
                new TenantContext { TenantGuid = Guid.NewGuid().ToString() },
                blobStorageProvider.Object,
                mediator.Object,
                dateTimeProviderMock.Object,
                new Mock<ILogger>().Object,
                "azure", "gpt-image-2", "agentType");
        }

        [Fact]
        public async Task EditImageAsync_NoChatHistory_ReturnsErrorHtml()
        {
            var chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = null };
            var service = new Mock<IImageEditService>();

            var result = await BuildPlugin(chatContext, service).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing failed", result);
            service.Verify(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            service.Verify(x => x.EditImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EditImageAsync_EmptyChatHistory_ReturnsErrorHtml()
        {
            var chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = new ChatHistory() };
            var service = new Mock<IImageEditService>();

            var result = await BuildPlugin(chatContext, service).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing failed", result);
        }

        [Fact]
        public async Task EditImageAsync_HistoryHasTextOnlyMessages_ReturnsErrorHtml()
        {
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, "Hello, can you edit my image?"));

            var chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = chatHistory };
            var service = new Mock<IImageEditService>();

            var result = await BuildPlugin(chatContext, service).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing failed", result);
        }

        [Fact]
        public async Task EditImageAsync_MultipleImagesInHistory_UsesLastOne()
        {
            var chatHistory = new ChatHistory();
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, items: new ChatMessageContentItemCollection
            {
                new ImageContent(new Uri(FirstImageUrl))
            }));
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, items: new ChatMessageContentItemCollection
            {
                new ImageContent(new Uri(LastImageUrl))
            }));

            var chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = chatHistory };
            var service = new Mock<IImageEditService>();
            service
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1 }, "image/png", "png", new AITokenUsage()));

            await BuildPlugin(chatContext, service).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            service.Verify(x => x.EditImageAsync(LastImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            service.Verify(x => x.EditImageAsync(FirstImageUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EditImageAsync_ImageContentHasBothUriAndData_PrefersUri_CallsUrlOverload()
        {
            var chatHistory = new ChatHistory();
            var imageContent = new ImageContent(new System.ReadOnlyMemory<byte>(FakeImageBytes), "image/png")
            {
                Uri = new Uri(LastImageUrl)
            };
            chatHistory.Add(new ChatMessageContent(AuthorRole.User, items: new ChatMessageContentItemCollection
            {
                imageContent
            }));

            var chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = chatHistory };
            var service = new Mock<IImageEditService>();
            service
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1 }, "image/png", "png", new AITokenUsage()));

            await BuildPlugin(chatContext, service).EditImageAsync(Instruction, cancellationToken: TestContext.Current.CancellationToken);

            service.Verify(x => x.EditImageAsync(LastImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            service.Verify(x => x.EditImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static ChatMessageContent BuildNamedImageMessage(string fileName, string mimeType, Uri uri)
        {
            var items = new ChatMessageContentItemCollection
            {
                new TextContent($"FileName: '{fileName}'\nFileType: '{mimeType}'"),
                new ImageContent { Uri = uri }
            };

            var metadata = new Dictionary<string, object>
            {
                { CommonMetadataConsts.FileName, fileName },
                { CommonMetadataConsts.FileType, mimeType }
            };

            return new ChatMessageContent(AuthorRole.User, items, metadata: metadata);
        }

        [Fact]
        public async Task EditImageAsync_FileNameJsonMatchesOlderImage_UsesThatImageNotMostRecent()
        {
            var chatHistory = new ChatHistory
            {
                BuildNamedImageMessage("first.jpg", "image/jpeg", new Uri(FirstImageUrl)),
                BuildNamedImageMessage("last.jpg", "image/jpeg", new Uri(LastImageUrl))
            };

            var chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = chatHistory };
            var service = new Mock<IImageEditService>();
            service
                .Setup(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImageEditResult(new byte[] { 1 }, "image/png", "png", new AITokenUsage()));

            var fileNameJson = "{\"fileName\":\"first.jpg\",\"fileMimeType\":\"image/jpeg\"}";

            await BuildPlugin(chatContext, service).EditImageAsync(Instruction, fileNameJson: fileNameJson, cancellationToken: TestContext.Current.CancellationToken);

            service.Verify(x => x.EditImageAsync(FirstImageUrl, Instruction, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            service.Verify(x => x.EditImageAsync(LastImageUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EditImageAsync_FileNameJsonNoMatch_ReturnsDistinctErrorHtml()
        {
            var chatHistory = new ChatHistory
            {
                BuildNamedImageMessage("last.jpg", "image/jpeg", new Uri(LastImageUrl))
            };

            var chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = chatHistory };
            var service = new Mock<IImageEditService>();

            var fileNameJson = "{\"fileName\":\"does-not-exist.jpg\",\"fileMimeType\":\"image/jpeg\"}";

            var result = await BuildPlugin(chatContext, service).EditImageAsync(Instruction, fileNameJson: fileNameJson, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing failed", result);
            Assert.Contains("No image found matching", result);
            service.Verify(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EditImageAsync_FileNameJsonInvalidJson_ReturnsErrorHtml()
        {
            var chatHistory = new ChatHistory
            {
                BuildNamedImageMessage("last.jpg", "image/jpeg", new Uri(LastImageUrl))
            };

            var chatContext = new ChatContext { SessionId = SessionId, CurrentChatHistory = chatHistory };
            var service = new Mock<IImageEditService>();

            var result = await BuildPlugin(chatContext, service).EditImageAsync(Instruction, fileNameJson: "not valid json", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("Image editing failed", result);
            service.Verify(x => x.EditImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
