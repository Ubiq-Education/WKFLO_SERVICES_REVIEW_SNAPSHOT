using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Mappings;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Task 8215: the chat shows an image (returned by the agent or uploaded by the user) without its
    /// "FileName/FileType" label, while the label stays in the chat history the model reads.
    /// </summary>
    public class ChatImageLabelTests
    {
        private static readonly Uri ImageUri = new("https://blob.example/chat-images/edited.png?sas");

        private readonly IWkfloMapper _mapper = WkfloMapperExtensions.CreateMapper();

        private static ChatMessageContent ImageMessage(string label) => new(AuthorRole.User,
        [
            new TextContent(label),
            new ImageContent { Uri = ImageUri }
        ]);

        public static TheoryData<string> Labels => new()
        {
            ChatImageLabel.ForReturnedImage("edited.png", "image/png"),
            ChatImageLabel.ForUploadedImage("photo.png", "image/png")
        };

        [Fact]
        public void Labels_AreTheTextTheToolsDescribe()
        {
            Assert.Equal("FileName: 'edited.png'\nFileType: 'image/png'", ChatImageLabel.ForReturnedImage("edited.png", "image/png"));
            Assert.Equal("\n FileName: 'photo.png'\n FileType: 'image/png'", ChatImageLabel.ForUploadedImage("photo.png", "image/png"));
        }

        [Theory]
        [MemberData(nameof(Labels))]
        public void ContentForChat_LabelledImage_IsEmpty_AndTheHistoryKeepsTheLabel(string label)
        {
            var message = ImageMessage(label);

            Assert.Equal(string.Empty, ChatImageLabel.ContentForChat(message));
            Assert.Equal(label, message.Content);
        }

        [Theory]
        [InlineData("FileName: 'edited.png'\nFileType: 'image/png'")]
        [InlineData("\n FileName: 'photo.png'\n FileType: 'image/png'")]
        [InlineData("Here is your image")]
        public void ContentForChat_MessageWithoutAnImage_IsUnchanged(string text)
        {
            Assert.Equal(text, ChatImageLabel.ContentForChat(new ChatMessageContent(AuthorRole.User, text)));
        }

        [Theory]
        [InlineData("FileName: 'edited.png'\nFileType: 'image/png'\nAnything else?")]
        [InlineData("Here is the edited version")]
        public void ContentForChat_ImageWithOtherText_IsUnchanged(string text)
        {
            var message = ImageMessage(text);

            Assert.Equal(text, ChatImageLabel.ContentForChat(message));
        }

        [Theory]
        [MemberData(nameof(Labels))]
        public void ContentForChat_LabelPlusAnotherTextItem_IsUnchanged(string label)
        {
            var message = new ChatMessageContent(AuthorRole.User,
            [
                new TextContent(label),
                new TextContent("Here is the edited version"),
                new ImageContent { Uri = ImageUri }
            ]);

            Assert.Equal(label, ChatImageLabel.ContentForChat(message));
        }

        [Fact]
        public void ContentForChat_Null_IsNull()
        {
            Assert.Null(ChatImageLabel.ContentForChat(null));
        }

        [Theory]
        [MemberData(nameof(Labels))]
        public void ChatHistoryResults_LabelledImage_HaveNoContent_ButKeepTheImage(string label)
        {
            var publicResult = _mapper.Map<PublicAgentChatMessageResult>(ImageMessage(label));
            var tenantResult = _mapper.Map<TenantAgentChatMessageResult>(ImageMessage(label));

            Assert.Equal(string.Empty, publicResult.Content);
            Assert.Equal(ImageUri.ToString(), publicResult.FileUrl);
            Assert.Equal(string.Empty, tenantResult.Content);
            Assert.Equal(ImageUri.ToString(), tenantResult.FileUrl);
        }
    }
}
