using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class PriorityQAToolConfig
    {
        [JsonProperty("items")]
        public List<PriorityQAItem> Items { get; set; } = [];
    }

    public class PriorityQAItem
    {
        [JsonProperty("question")]
        public string Question { get; set; }

        [JsonProperty("answer")]
        public string Answer { get; set; }

        [JsonProperty("links")]
        public List<string> Links { get; set; } = [];
    }
}
