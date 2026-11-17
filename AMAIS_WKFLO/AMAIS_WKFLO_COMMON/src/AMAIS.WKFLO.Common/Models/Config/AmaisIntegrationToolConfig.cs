using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class AmaisIntegrationToolConfig
    {
        [JsonProperty("isVoiceEnabled")]
        public bool IsVoiceEnabled { get; set; }

        [JsonProperty("areSkillsEnabled")]
        public bool AreSkillsEnabled { get; set; }

        [JsonProperty("voiceName")]
        public string VoiceName { get; set; }

        [JsonProperty("voiceLanguageCode")]
        public string VoiceLanguageCode { get; set; }
    }
}
