using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class AgentChatSetting : Entity<int>
    {
        public string HeaderTitle { get; set; }
        public string ChatWidth { get; set; }
        public string PrimaryColor { get; set; }
        public string ForegroundColor { get; set; }
        public string BubbleStyle { get; set; }
        public string FloatingPosition { get; set; }
        public string LauncherStyle { get; set; }
        public string LauncherText { get; set; }
        public string BubbleIconPath { get; set; }
        public string ChatbotIconPath { get; set; }
        public string CssUrl { get; set; }
        public string WelcomeMessage { get; set; }
        public string FooterMessage { get; set; }
        public ICollection<PredefinedPrompt> PredefinedPrompts { get; set; }
        public bool ShowDescription { get; set; }
        public bool ChainOfThought { get; set; }
        public bool IsWidget { get; set; }
        public string ApiEndpoint { get; set; }
        public string SecurityConfiguration { get; set; } // JSON serialized configuration
        public bool IsShareable { get; set; }
        public string ShareId { get; set; }
        public int AgentId { get; set; }
        public virtual Agent Agent { get; set; }
    }
}