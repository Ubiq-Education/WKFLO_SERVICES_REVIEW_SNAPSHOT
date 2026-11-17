using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class ImageGenerationToolConfig
    {
        /// <summary>"azure" or "google"</summary>
        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }

        /// <summary>Azure OpenAI resource endpoint (Azure only).</summary>
        [JsonProperty("endpoint")]
        public string Endpoint { get; set; }

        /// <summary>Azure deployment name, e.g. "gpt-image-2" (Azure only).</summary>
        [JsonProperty("deploymentName")]
        public string DeploymentName { get; set; }

        /// <summary>Azure API version, defaults to 2025-04-01-preview (Azure only).</summary>
        [JsonProperty("apiVersion")]
        public string ApiVersion { get; set; } = "2025-04-01-preview";

        /// <summary>Model name override. For Google: "imagen-3.0-edit-v1" or "gemini-2.0-flash-preview-image-generation".</summary>
        [JsonProperty("model")]
        public string Model { get; set; }

        /// <summary>"low", "medium", or "high". Defaults to "high".</summary>
        [JsonProperty("quality")]
        public string Quality { get; set; } = "high";

        /// <summary>
        /// Output aspect ratio, a fixed choice that overrides the input image's ratio. Leave empty for "Auto (match input)":
        /// edits then keep the input image's ratio. Google accepts "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16",
        /// "16:9", "21:9"; Azure maps it to the nearest size (1024x1024, 1536x1024, 1024x1536).
        /// </summary>
        [JsonProperty("aspectRatio")]
        public string AspectRatio { get; set; }
    }
}
