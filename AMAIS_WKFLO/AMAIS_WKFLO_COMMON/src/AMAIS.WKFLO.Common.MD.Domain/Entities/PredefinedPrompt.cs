using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class PredefinedPrompt : Entity<int>
    {
        public string Title { get; set; }
        public string Prompt { get; set; }
        public int AgentChatSettingId { get; set; }
        public AgentChatSetting AgentChatSetting { get; set; }
    }
}
