using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class AgentChatSettingUpdateResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
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
        public IList<PredefinedPromptModel> PredefinedPrompts { get; set; }
        public bool ShowDescription { get; set; }
        public bool ChainOfThought { get; set; }
        public string ApiEndpoint { get; set; }
        public bool IsShareable { get; set; }
        public string ShareId { get; set; }
        public int AgentId { get; set; }
        public string SecurityConfiguration { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<AgentChatSetting, AgentChatSettingUpdateResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<AgentChatSetting, AgentChatSettingUpdateResult>();
        }
    }
}
