using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions
{
    /// <summary>
    /// WI 7447: one entry of <see cref="Entities.Tool.DiscoveredFunctions"/> - a function exactly as the MCP
    /// server reported it in <c>tools/list</c> (name/description are the server's own).
    /// </summary>
    public class McpFunctionDescriptor
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>The function's JSON schema for its arguments, kept as raw JSON.</summary>
        [JsonProperty("inputSchema")]
        public JToken InputSchema { get; set; }

        /// <summary>
        /// True while no admin has saved the function allowlist since this function first appeared in a
        /// snapshot (shown as "New"). Cleared for every listed function when the allowlist is saved.
        /// </summary>
        [JsonProperty("isNew")]
        public bool IsNew { get; set; }

        /// <summary>
        /// WI 7726: optional friendly name chosen by an admin for the AMAIS/WKFLO UIs (display only - the
        /// function is still identified by <see cref="Name"/> and the LLM still receives <see cref="Description"/>).
        /// Null when not set; consumers fall back to <see cref="Name"/>.
        /// </summary>
        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        /// <summary>WI 7726: optional friendly description (display only). Null when not set; consumers fall back to <see cref="Description"/>.</summary>
        [JsonProperty("displayDescription", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayDescription { get; set; }

        /// <summary>WI 7726: maximum length of <see cref="DisplayName"/>.</summary>
        public const int DisplayNameMaxLength = 100;

        /// <summary>WI 7726: maximum length of <see cref="DisplayDescription"/>.</summary>
        public const int DisplayDescriptionMaxLength = 500;

        /// <summary>WI 7726: trims a friendly value; blank/whitespace becomes null ("not set").</summary>
        public static string NormalizeDisplayValue(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// WI 7447/7575: tolerant (de)serialization of the JSON columns added for tool permissions. A malformed
    /// stored value never throws - it reads back as "nothing saved" (no MCP function enabled, which
    /// <see cref="McpFunctionAllowlist"/> treats as "all functions allowed" - Bug 7856; every permission Block).
    /// </summary>
    public static class ToolPermissionJson
    {
        public static List<McpFunctionDescriptor> ParseDiscoveredFunctions(string json) =>
            TryDeserialize<List<McpFunctionDescriptor>>(json)?.Where(f => !string.IsNullOrWhiteSpace(f?.Name)).ToList() ?? [];

        public static string SerializeDiscoveredFunctions(IEnumerable<McpFunctionDescriptor> functions) =>
            JsonConvert.SerializeObject((functions ?? []).ToList());

        /// <summary>
        /// Enabled MCP function names (ordinal, case-sensitive: MCP names are case-sensitive). To decide whether a
        /// function is allowed use <see cref="McpFunctionAllowlist"/>: an empty set means every function is allowed.
        /// </summary>
        public static HashSet<string> ParseEnabledFunctions(string json) =>
            new((TryDeserialize<List<string>>(json) ?? []).Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);

        public static string SerializeEnabledFunctions(IEnumerable<string> names) =>
            JsonConvert.SerializeObject((names ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList());

        /// <summary>Per-function permissions; invalid values are dropped (so they read back as <see cref="ToolPermissionValues.Default"/>, Block).</summary>
        public static Dictionary<string, string> ParseFunctionPermissions(string json)
        {
            var parsed = TryDeserialize<Dictionary<string, string>>(json) ?? [];
            return parsed
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && ToolPermissionValues.IsValid(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }

        /// <summary>Null when the map is empty, so an agent with nothing saved keeps a NULL column.</summary>
        public static string SerializeFunctionPermissions(IReadOnlyDictionary<string, string> permissions) =>
            permissions == null || permissions.Count == 0 ? null : JsonConvert.SerializeObject(permissions);

        private static T TryDeserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
