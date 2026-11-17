using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class HttpCallToolConfig
    {
        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("headers")]
        public Dictionary<string, string> Headers { get; set; } = [];

        [JsonProperty("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = [];

        [JsonProperty("urlParams")]
        public Dictionary<string, string> UrlParams { get; set; } = [];

        [JsonProperty("body")]
        public string Body { get; set; }

        [JsonProperty("parameterConfig")]
        public Dictionary<string, object> ParameterConfig { get; set; } = [];

        /// <summary>
        /// Named input definitions. Each entry may contain a <c>{{key}}</c> placeholder that is
        /// substituted into <see cref="Body"/> or <see cref="Parameters"/> at execution time.
        /// </summary>
        [JsonProperty("inputs")]
        public Dictionary<string, HttpCallInputDefinition> Inputs { get; set; } = [];

        /// <summary>
        /// Maximum number of characters to return from the HTTP response body.
        /// Responses exceeding this limit are truncated with a notice appended.
        /// Defaults to 50 000 characters (~12 500 tokens) when null or omitted.
        /// </summary>
        [JsonProperty("maxResponseChars")]
        public int? MaxResponseChars { get; set; }
    }
}
