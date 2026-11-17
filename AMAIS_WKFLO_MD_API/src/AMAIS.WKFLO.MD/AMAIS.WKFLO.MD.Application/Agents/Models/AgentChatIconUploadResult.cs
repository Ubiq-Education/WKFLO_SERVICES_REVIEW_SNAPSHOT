namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    /// <summary>
    /// Result of uploading a chat icon image. Populated directly in the handler from
    /// IImageStorageService's result — there is no domain entity to map from since the endpoint
    /// does not persist anything onto AgentChatSetting.
    /// </summary>
    public class AgentChatIconUploadResult
    {
        public string RelativePath { get; set; }
        public string AbsoluteUrl { get; set; }
    }
}
