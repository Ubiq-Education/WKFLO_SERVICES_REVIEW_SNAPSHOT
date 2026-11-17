using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Linq;
using System.Text.Json;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.Services.ChatStore
{
    /// <summary>
    /// GATE TEST — must pass before any implementation of task 5433.
    /// Verifies that ImageContent(Uri) survives a JSON round-trip through
    /// the same JsonSerializer calls used by ChatStoreService.
    /// If these tests fail, the whole "store URL instead of bytes" approach
    /// cannot work and must be reconsidered before proceeding.
    /// </summary>
    public class ChatHistoryImageContentSerializationTests
    {
        [Fact]
        public void ImageContentWithUri_SurvivesJsonRoundTrip_UriPreserved()
        {
            // Arrange
            var imageUri = new Uri("https://mystorageaccount.blob.core.windows.net/chat-images/session-1/abc-photo.png");
            var history = new ChatHistory();
            var items = new ChatMessageContentItemCollection
            {
                new TextContent("Here is the image"),
                new ImageContent(imageUri)
            };
            history.Add(new ChatMessageContent(AuthorRole.User, items));

            // Act — same serializer call as ChatStoreService.SaveChatAsync line 42
            var json = JsonSerializer.Serialize(history);

            // Assert — json must not contain the raw URI embedded as bytes
            Assert.Contains("chat-images", json);

            // Act — same deserializer call as ChatStoreService.LoadChatAsync line 59
            var restored = JsonSerializer.Deserialize<ChatHistory>(json);

            // Assert
            Assert.NotNull(restored);
            Assert.Single(restored);

            var message = restored[0];
            var imageItem = message.Items.OfType<ImageContent>().FirstOrDefault();
            Assert.NotNull(imageItem);

            // The URI must round-trip intact
            Assert.NotNull(imageItem.Uri);
            Assert.Equal(imageUri, imageItem.Uri);

            // Data must NOT be populated (no bytes in history)
            Assert.Null(imageItem.Data);
        }

        [Fact]
        public void ImageContentWithUri_JsonDoesNotContainBase64()
        {
            // Arrange — confirm the serialized form is a URL string, not base64 bytes
            var imageUri = new Uri("https://mystorageaccount.blob.core.windows.net/chat-images/session-1/abc-photo.png");
            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.User, new ChatMessageContentItemCollection
            {
                new ImageContent(imageUri)
            }));

            // Act
            var json = JsonSerializer.Serialize(history);

            // Assert — the JSON must not contain a base64 data payload
            // A base64-encoded 1-byte image starts with "iVBOR" (PNG) or "/9j/" (JPEG).
            // If the URL is stored correctly, no such prefix appears.
            Assert.DoesNotContain("iVBOR", json);  // PNG magic
            Assert.DoesNotContain("/9j/", json);   // JPEG magic
            Assert.DoesNotContain("data:image", json);
        }

        [Fact]
        public void ImageContentWithUri_UriPropertyIsSettable()
        {
            // Arrange — verify item.Uri = newUri works (required by LoadChatAsync SAS replacement)
            var originalUri = new Uri("https://mystorageaccount.blob.core.windows.net/chat-images/session-1/abc-photo.png");
            var sasUri = new Uri("https://mystorageaccount.blob.core.windows.net/chat-images/session-1/abc-photo.png?sv=2022&se=...&sig=abc");

            var item = new ImageContent(originalUri);

            // Act — this is the exact operation LoadChatAsync will do
            item.Uri = sasUri;

            // Assert
            Assert.Equal(sasUri, item.Uri);
        }
    }
}
