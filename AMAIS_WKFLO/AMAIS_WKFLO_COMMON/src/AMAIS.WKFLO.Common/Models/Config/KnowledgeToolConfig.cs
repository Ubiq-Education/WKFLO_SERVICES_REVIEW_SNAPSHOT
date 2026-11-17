using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class KnowledgeToolConfig
    {
        [JsonProperty("tags")]
        public List<string> Tags { get; set; }
    }
}
