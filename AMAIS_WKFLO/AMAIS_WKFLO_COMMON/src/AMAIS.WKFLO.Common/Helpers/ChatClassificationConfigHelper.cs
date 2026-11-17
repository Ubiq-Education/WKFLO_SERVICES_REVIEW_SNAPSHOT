using AMAIS.WKFLO.Common.Models.Config;

namespace AMAIS.WKFLO.Common.Helpers
{
    /// <summary>
    /// Reads the "topic version" to send with a "Chatbot Message Topic" DXP event from a ChatClassification
    /// tool's current config, replacing the SHA-256 hash this codebase used to compute at classification time
    /// (see the retired <c>TopicVersionHelper</c>). The version is now a persisted, auto-incrementing integer
    /// (<see cref="ChatClassificationToolConfig.Version"/>), bumped by <c>UpdateTenantToolCommandHandler</c>
    /// whenever the topic configuration actually changes.
    /// </summary>
    public static class ChatClassificationConfigHelper
    {
        /// <summary>
        /// Returns the config's current version as a string, defensively treating any non-positive stored
        /// value (0 or less — should not occur in practice, but old/corrupted data is possible) the same as
        /// a missing one: version 1. A config whose JSON never had a "version" property already deserializes
        /// with <see cref="ChatClassificationToolConfig.Version"/> at its default of 1, so this only matters
        /// for defensive purposes.
        /// </summary>
        public static string ResolveTopicVersion(ChatClassificationToolConfig config) =>
            (config.Version > 0 ? config.Version : 1).ToString();
    }
}
