using Microsoft.SemanticKernel;
using System.Text.RegularExpressions;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    /// <summary>
    /// The FileName/FileType text stored next to an image in the chat history: an image the agent returns
    /// (generated or edited) or one the user uploads. The model needs it: edit_image, AgentEventPublisher and the
    /// Page/Post/Gallery tools identify a file by the name and type "shown earlier in this conversation". The chat
    /// UI does not show it (Task 8215), so messages sent to the UI use <see cref="ContentForChat"/> instead of
    /// <see cref="ChatMessageContent.Content"/>.
    /// </summary>
    public static class ChatImageLabel
    {
        // Returned: "FileName: '...'\nFileType: '...'". Uploaded: "\n FileName: '...'\n FileType: '...'".
        private static readonly Regex Label = new(@"\A(?:\n )?FileName: '[^\n]*'\n ?FileType: '[^\n]*'\z", RegexOptions.Compiled);

        /// <summary>The label of an image the agent returns.</summary>
        public static string ForReturnedImage(string fileName, string mimeType) => $"FileName: '{fileName}'\nFileType: '{mimeType}'";

        /// <summary>The label of an image the user uploads.</summary>
        public static string ForUploadedImage(string fileName, string mimeType) => $"\n FileName: '{fileName}'\n FileType: '{mimeType}'";

        /// <summary>
        /// The message text for the chat UI: empty for an image message whose only text is its label, otherwise
        /// <see cref="ChatMessageContent.Content"/> unchanged. Also applies to conversations saved before Task 8215.
        /// </summary>
        public static string ContentForChat(ChatMessageContent message) =>
            IsLabelledImage(message) ? string.Empty : message?.Content;

        // Content is only the first text item, so the label must also be the message's only text item.
        private static bool IsLabelledImage(ChatMessageContent message) =>
            message?.Content is { } content && Label.IsMatch(content) &&
            message.Items.OfType<TextContent>().Count() == 1 && message.Items.OfType<ImageContent>().Any();
    }
}
