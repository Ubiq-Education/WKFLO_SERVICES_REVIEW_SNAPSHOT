using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class ChatClassificationToolConfig
    {
        [JsonProperty("topics")]
        public List<ClassificationTopic> Topics { get; set; } = [];

        /// <summary>
        /// Auto-incrementing topic-set version, persisted alongside the tool's config. Bumped by
        /// <c>UpdateTenantToolCommandHandler</c> whenever the topic configuration actually changes.
        /// Tools configured before this field existed have no "version" property in their stored JSON,
        /// which Newtonsoft leaves at this property's default — so a missing/null value is version 1.
        /// </summary>
        [JsonProperty("version")]
        public int Version { get; set; } = 1;
    }

    public class ClassificationTopic
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; } = true;

        [JsonProperty("handoffMessage")]
        public string HandoffMessage { get; set; }
    }
}
