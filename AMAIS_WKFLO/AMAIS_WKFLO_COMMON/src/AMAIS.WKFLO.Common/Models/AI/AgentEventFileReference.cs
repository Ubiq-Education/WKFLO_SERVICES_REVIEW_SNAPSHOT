namespace AMAIS.WKFLO.Common.Models.AI
{
    /// <summary>
    /// A reference to a file already present in the conversation's chat history, named by
    /// <see cref="FileName"/> and <see cref="FileMimeType"/>, resolved against
    /// <c>ChatContext.CurrentChatHistory</c> by matching both fields exactly — the same convention
    /// <c>PostPlugin.UpdatePostImageByImage</c> uses with flat string parameters instead. Two consumers
    /// today: <c>AgentEventPublisherPlugin.PublishAgentEventAsync</c>, where the LLM supplies a JSON
    /// array of these to forward one or more attachments, and
    /// <c>ImageGenerationPlugin.EditImageAsync</c>, where the LLM supplies a single JSON object of this
    /// shape to target a specific (non-most-recent) uploaded image for editing.
    /// </summary>
    public class AgentEventFileReference
    {
        public string FileName { get; set; }

        public string FileMimeType { get; set; }
    }
}
