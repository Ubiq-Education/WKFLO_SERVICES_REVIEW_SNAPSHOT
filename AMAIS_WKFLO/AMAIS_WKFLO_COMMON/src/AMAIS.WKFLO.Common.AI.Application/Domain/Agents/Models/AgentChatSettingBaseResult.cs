namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models
{
    public class AgentChatSettingBaseResult
    {
        public bool IsVoiceChatEnabled { get; set; }
        public bool AreSkillsEnabled { get; set; }
        public string VoiceName { get; set; }
        public string VoiceLanguageCode { get; set; }
        public bool CanSelectFeature { get; set; }

        /// <summary>US 7974: the agent's tenant's date/time display settings (or their defaults). Additive.</summary>
        public TenantDisplaySettingsResult DisplaySettings { get; set; }

        /// <summary>
        /// True when the agent is tagged "Maisy". The explicit flag the chat UI gates MAISY-only behaviour on
        /// (conversations list, new chat, top bar). <see cref="CanSelectFeature"/> carries the same value today
        /// but describes one feature (the feature-selection button), so it must not be reused as the Maisy check.
        /// </summary>
        public bool IsMaisyAgent { get; set; }
    }
}