using AMAIS.WKFLO.Common.Infrastructure.Services.ChatStore;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.Services.ChatStore
{
    /// <summary>
    /// US 7974 / Task 7979: <see cref="ChatStoreService.LoadChatAsync"/> swaps stored chat-images URLs for SAS URLs.
    /// It must find the blob for both the legacy session-rooted names and the new tenant-prefixed names.
    /// </summary>
    public class ChatStoreServiceChatImageUrlTests
    {
        private const string StorageBase = "https://account.blob.core.windows.net/chat-images/";

        private readonly Mock<IBlobStorageProvider> _blobStorageProvider = new();
        private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
        private readonly ChatStoreService _service;

        public ChatStoreServiceChatImageUrlTests()
        {
            _dateTimeProvider.Setup(x => x.DateTimeOffsetUtcNow()).Returns(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            _service = new ChatStoreService(_blobStorageProvider.Object, new Mock<ILogger<ChatStoreService>>().Object, _dateTimeProvider.Object);
        }

        [Theory]
        [InlineData("session-1/0f8fad5b-d9cb-469f-a165-70867728950e-photo.png")]
        [InlineData("edited/0f8fad5b-d9cb-469f-a165-70867728950e.png")]
        [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301/session-1/0f8fad5b-d9cb-469f-a165-70867728950e-photo.png")]
        [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301/edited/0f8fad5b-d9cb-469f-a165-70867728950e.png")]
        public async Task LoadChatAsync_StoredImageUrl_ChecksThatBlobAndReplacesUrlWithSas(string blobName)
        {
            var blobUri = new Uri(StorageBase + blobName);
            var sasUrl = blobUri + "?sig=abc";
            ArrangeStoredChat(blobUri);
            _blobStorageProvider.Setup(x => x.ExistsAsync("chat-images", blobName, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _blobStorageProvider.Setup(x => x.GenerateSasUrlAsync(blobUri, It.IsAny<TimeSpan>())).ReturnsAsync(sasUrl);

            var (_, _, history, _) = await _service.LoadChatAsync("tenant", 1, "session-1", null);

            var image = Assert.Single(history.Single().Items.OfType<ImageContent>());
            Assert.Equal(new Uri(sasUrl), image.Uri);
            _blobStorageProvider.Verify(x => x.ExistsAsync("chat-images", blobName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task LoadChatAsync_TenantPrefixedImageGone_ReplacedWithPlaceholderText()
        {
            const string blobName = "3f2504e0-4f89-11d3-9a0c-0305e82c3301/session-1/0f8fad5b-d9cb-469f-a165-70867728950e-photo.png";
            ArrangeStoredChat(new Uri(StorageBase + blobName));
            _blobStorageProvider.Setup(x => x.ExistsAsync("chat-images", blobName, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var (_, _, history, _) = await _service.LoadChatAsync("tenant", 1, "session-1", null);

            var message = history.Single();
            Assert.Empty(message.Items.OfType<ImageContent>());
            Assert.Contains(message.Items.OfType<TextContent>(), t => t.Text.Contains("no longer available"));
        }

        private void ArrangeStoredChat(Uri imageUri)
        {
            var history = new ChatHistory
            {
                new ChatMessageContent(AuthorRole.User, new ChatMessageContentItemCollection { new ImageContent(imageUri) })
            };
            var persisted = new PersistedChat
            {
                AgentId = 1,
                SessionId = "session-1",
                SerializedChatHistory = JsonSerializer.Serialize(history)
            };

            _blobStorageProvider.Setup(x => x.ExistsAsync("chat-history", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _blobStorageProvider
                .Setup(x => x.DownloadAsync("chat-history", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(persisted))));
        }
    }
}
