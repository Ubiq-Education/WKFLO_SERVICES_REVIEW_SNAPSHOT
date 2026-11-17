namespace AMAIS.WKFLO.MD.Application.Tools.Models
{
    /// <summary>
    /// A single Chatbot Topic's current data, resolved from its agent's Chat_Classification tool config
    /// (see <see cref="AMAIS.WKFLO.Common.Models.Config.ClassificationTopic"/>). <see cref="Version"/> is the
    /// tool config's current topic-set version (see <see cref="AMAIS.WKFLO.Common.Helpers.ChatClassificationConfigHelper"/>),
    /// letting a caller (e.g. AMAIS_DXP) tell apart a Topic snapshot taken under an older config from the
    /// current one — Topic <see cref="Id"/> is stable across renames, but <see cref="Name"/> is not.
    /// </summary>
    public class ChatClassificationTopicGetResult
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public string HandoffMessage { get; set; }
        public int AgentId { get; set; }
        public int ToolId { get; set; }
        public string Version { get; set; }
        public DateTimeOffset ToolLastUpdatedOn { get; set; }
    }
}
