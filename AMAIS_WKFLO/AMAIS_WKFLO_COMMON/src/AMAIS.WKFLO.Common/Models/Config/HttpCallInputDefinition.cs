using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class HttpCallInputDefinition
    {
        /// <summary>Human-readable label shown in the UI.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Passed to the LLM as the parameter description so it knows what value to supply.</summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>Logical type hint: string | number | boolean | array | object</summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "string";

        [JsonProperty("required")]
        public bool Required { get; set; }

        /// <summary>Current configured value — used when the LLM does not supply one.</summary>
        [JsonProperty("value")]
        public string Value { get; set; }

        /// <summary>Fallback value when neither the LLM nor <see cref="Value"/> provides one.</summary>
        [JsonProperty("defaultValue")]
        public string DefaultValue { get; set; }

        [JsonProperty("hasDefaultValue")]
        public bool HasDefaultValue { get; set; }
    }
}
