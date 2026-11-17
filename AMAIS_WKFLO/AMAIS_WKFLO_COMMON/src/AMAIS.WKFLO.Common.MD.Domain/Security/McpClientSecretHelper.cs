using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.MD.Domain.Security
{
    /// <summary>
    /// Handles the preconfigured OAuth client secret (<c>McpServerConfig.ClientSecret</c>) carried inside an MCP
    /// tool's <c>Config</c> JSON: encrypted at rest, never returned to the browser, and merged on write so the
    /// admin UI never has to resend it (US 7849).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored form is <see cref="EncryptedPrefix"/> + <see cref="IMcpTokenProtector.Protect"/> output. The prefix
    /// lets readers tell an encrypted value from a legacy plaintext one saved before this existed: legacy values
    /// are passed through as-is by <see cref="Resolve"/> and re-encrypted on the tool's next save — no data
    /// migration needed.
    /// </para>
    /// <para>
    /// Write contract (see <see cref="MergeOnWrite"/>): a blank/absent <c>clientSecret</c> keeps the stored
    /// value; a non-empty one replaces it; <c>clearClientSecret: true</c> removes it; <c>hasClientSecret</c> and
    /// <c>clearClientSecret</c> are response-only/write-only flags and are never persisted. Neither flag is a
    /// property of <c>McpServerConfig</c> on purpose — that type gets re-serialized by publish/provisioning and
    /// must stay the pure runtime shape.
    /// </para>
    /// </remarks>
    public static class McpClientSecretHelper
    {
        public const string EncryptedPrefix = "enc:v1:";

        public const string ClientSecretKey = "clientSecret";
        public const string HasClientSecretKey = "hasClientSecret";
        public const string ClearClientSecretKey = "clearClientSecret";
        public const string ClientIdKey = "clientId";
        public const string EndpointKey = "endpoint";
        public const string AuthTypeKey = "authType";

        // Config JSON is round-tripped through JObject; without this, ISO-looking strings would be re-written
        // as Newtonsoft's own DateTime format.
        private static readonly JsonSerializerSettings ParseSettings = new() { DateParseHandling = DateParseHandling.None };

        /// <summary>Replacement written over redacted values. See <see cref="SensitiveDataRedactor"/>.</summary>
        public const string RedactedValue = SensitiveDataRedactor.RedactedValue;

        /// <summary>
        /// Masks every non-empty <c>clientSecret</c> (and the other always-sensitive OAuth names listed in
        /// <see cref="SensitiveDataRedactor.AlwaysSensitiveNames"/>) in <paramref name="text"/>, including inside JSON
        /// string properties such as a serialized <c>Config</c>. Used for request logs and the audit trail.
        /// </summary>
        public static string RedactSecrets(string text) => SensitiveDataRedactor.Redact(text);

        /// <summary>Encrypts a plaintext client secret into its stored form. Null/empty is returned unchanged.</summary>
        public static string Protect(IMcpTokenProtector protector, string plaintext)
        {
            ArgumentNullException.ThrowIfNull(protector);
            return string.IsNullOrEmpty(plaintext) ? plaintext : EncryptedPrefix + protector.Protect(plaintext);
        }

        /// <summary>
        /// Returns the plaintext client secret for a stored value: decrypts <see cref="EncryptedPrefix"/>ed values,
        /// passes legacy (unprefixed) plaintext through unchanged. Null/empty is returned unchanged. Throws when an
        /// encrypted value can't be decrypted (wrong key or tampered data) — callers must not silently fall back
        /// to sending ciphertext as a secret.
        /// </summary>
        public static string Resolve(IMcpTokenProtector protector, string storedValue)
        {
            ArgumentNullException.ThrowIfNull(protector);
            if (string.IsNullOrEmpty(storedValue) || !IsProtected(storedValue))
                return storedValue;

            return protector.Unprotect(storedValue[EncryptedPrefix.Length..]);
        }

        public static bool IsProtected(string storedValue) =>
            storedValue != null && storedValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Response-side redaction for a tool's <c>Config</c>: for an MCP tool, removes <c>clientSecret</c> and
        /// adds a read-only <c>hasClientSecret</c> flag. Fails closed for everything else: a non-MCP config never
        /// echoes a top-level <c>clientSecret</c> either (e.g. one left over from before a type change), and an
        /// unparseable config is passed through <see cref="SensitiveDataRedactor"/>'s pattern masking. Configs
        /// with nothing to remove are returned exactly as stored.
        /// </summary>
        public static string SanitizeForResponse(string toolType, string configJson)
        {
            if (!TryParse(configJson, out var config))
                return SensitiveDataRedactor.Redact(configJson);

            if (!IsMcp(toolType))
            {
                var stripped = RemoveAll(config, ClientSecretKey) | RemoveAll(config, HasClientSecretKey) | RemoveAll(config, ClearClientSecretKey);
                return stripped ? config.ToString(Formatting.None) : configJson;
            }

            // Case-insensitive, like McpServerConfig's own deserialization: a "ClientSecret" variant must be
            // stripped just the same.
            if (!IsOAuth2(config) && !HasVariant(config, ClientSecretKey) && !HasVariant(config, HasClientSecretKey) && !HasVariant(config, ClearClientSecretKey))
                return configJson;

            var hasSecret = !string.IsNullOrEmpty(GetString(config, ClientSecretKey));
            RemoveAll(config, ClientSecretKey);
            RemoveAll(config, ClearClientSecretKey);
            RemoveAll(config, HasClientSecretKey);
            config[HasClientSecretKey] = hasSecret;

            return config.ToString(Formatting.None);
        }

        /// <summary>
        /// Applies the client-secret write contract to an incoming MCP tool config before it is persisted.
        /// <paramref name="previousConfigJson"/> is the currently stored config (null on create).
        /// </summary>
        /// <remarks>
        /// Rules, in priority order:
        /// <list type="number">
        /// <item><c>authType</c> isn't OAuth2 → any secret is dropped (it's only ever used for OAuth2).</item>
        /// <item>Non-empty incoming <c>clientSecret</c> with an empty <c>clientId</c> → invalid.</item>
        /// <item>Non-empty incoming plaintext <c>clientSecret</c> → encrypted and stored (wins over
        /// <c>clearClientSecret</c>). An incoming value that is already in stored form (<see cref="EncryptedPrefix"/>)
        /// is never accepted as a new secret — it only ever comes from the stored config being echoed back (e.g. the
        /// PUT endpoint pre-filling omitted fields from the entity) — and is treated like a blank one.</item>
        /// <item><c>clearClientSecret: true</c> → stored secret removed.</item>
        /// <item><c>endpoint</c> or <c>clientId</c> changed → stored secret removed. Otherwise an admin could
        /// point the tool at a server they control and have the platform hand it the decrypted secret via
        /// <c>client_secret_post</c> without ever knowing the secret themselves.</item>
        /// <item>Otherwise the stored secret is kept (legacy plaintext re-encrypted).</item>
        /// </list>
        /// <see cref="McpClientSecretMergeResult.CredentialsChanged"/> is true when the <c>clientId</c> or the
        /// effective secret changed (including removal), so the caller can invalidate tokens issued under the old
        /// credentials.
        /// <para>
        /// <c>endpoint</c>, <c>clientId</c>, <c>clientSecret</c> and <c>authType</c> are read the way the runtime's
        /// case-insensitive <c>McpServerConfig</c> deserialization reads them (any casing, last occurrence wins),
        /// and the persisted JSON is normalised to exactly one canonical camelCase property each — otherwise a
        /// case-variant duplicate (<c>"Endpoint"</c> after <c>"endpoint"</c>) could redirect the runtime while the
        /// change detection here saw no change, or a <c>"ClientSecret"</c> could bypass encryption.
        /// </para>
        /// </remarks>
        public static McpClientSecretMergeResult MergeOnWrite(
            IMcpTokenProtector protector, string toolType, string newConfigJson, string previousConfigJson)
        {
            ArgumentNullException.ThrowIfNull(protector);

            if (!IsMcp(toolType))
                return McpClientSecretMergeResult.Unchanged(newConfigJson);

            // Fail closed: a config that isn't a JSON object can't be inspected, so it could carry a plaintext
            // secret past every rule here. The caller rejects it.
            if (!TryParse(newConfigJson, out var config))
                return McpClientSecretMergeResult.InvalidConfig(newConfigJson);

            JObject previous = null;
            if (!string.IsNullOrWhiteSpace(previousConfigJson))
                TryParse(previousConfigJson, out previous);

            var modified = false;
            foreach (var key in CanonicalKeys)
                modified |= Canonicalize(config, key);

            modified |= RemoveAll(config, HasClientSecretKey);

            var clearToken = Effective(config, ClearClientSecretKey);
            var clearRequested = clearToken != null && IsTrue(clearToken);
            modified |= RemoveAll(config, ClearClientSecretKey);

            var isOAuth2 = IsOAuth2(config);
            var incomingSecret = GetString(config, ClientSecretKey);
            var hasIncomingSecret = !string.IsNullOrWhiteSpace(incomingSecret) && !IsProtected(incomingSecret);

            var newClientId = Normalize(GetString(config, ClientIdKey));
            if (isOAuth2 && hasIncomingSecret && newClientId.Length == 0)
                return McpClientSecretMergeResult.SecretWithoutClientId(newConfigJson);

            var previousStoredSecret = previous == null ? null : GetString(previous, ClientSecretKey);
            var hadPreviousSecret = !string.IsNullOrEmpty(previousStoredSecret);

            var clientIdChanged = previous != null &&
                !string.Equals(Normalize(GetString(previous, ClientIdKey)), newClientId, StringComparison.Ordinal);
            var endpointChanged = previous != null &&
                !string.Equals(Normalize(GetString(previous, EndpointKey)), Normalize(GetString(config, EndpointKey)), StringComparison.Ordinal);

            string storedSecret;
            bool secretChanged;

            if (!isOAuth2)
            {
                storedSecret = null;
                secretChanged = hadPreviousSecret;
            }
            else if (hasIncomingSecret)
            {
                storedSecret = Protect(protector, incomingSecret);
                secretChanged = !hadPreviousSecret || !SameSecret(protector, previousStoredSecret, incomingSecret);
            }
            else if (clearRequested || clientIdChanged || endpointChanged)
            {
                storedSecret = null;
                secretChanged = hadPreviousSecret;
            }
            else if (hadPreviousSecret)
            {
                storedSecret = IsProtected(previousStoredSecret) ? previousStoredSecret : Protect(protector, previousStoredSecret);
                secretChanged = false;
            }
            else
            {
                storedSecret = null;
                secretChanged = false;
            }

            if (storedSecret == null)
            {
                modified |= config.Remove(ClientSecretKey);
            }
            else if (!string.Equals(GetString(config, ClientSecretKey), storedSecret, StringComparison.Ordinal))
            {
                config[ClientSecretKey] = storedSecret;
                modified = true;
            }

            return new McpClientSecretMergeResult(
                modified ? config.ToString(Formatting.None) : newConfigJson,
                // The endpoint counts too: tokens issued by the old server must never be sent to a new one,
                // even when the client id/secret stay the same (e.g. a public client with no secret).
                CredentialsChanged: clientIdChanged || endpointChanged || secretChanged,
                IsClientSecretWithoutClientId: false);
        }

        /// <summary>
        /// Removes the MCP client credential fields (<c>clientId</c>, <c>clientSecret</c>, <c>hasClientSecret</c>,
        /// <c>clearClientSecret</c>, any casing) from a config — used when a tool stops being an MCP tool, so a
        /// stored secret doesn't linger in a config nothing will ever manage or redact as MCP again. An
        /// unparseable config gets <see cref="SensitiveDataRedactor"/>'s pattern masking instead.
        /// </summary>
        public static string StripCredentials(string configJson)
        {
            if (!TryParse(configJson, out var config))
                return SensitiveDataRedactor.Redact(configJson);

            var stripped = RemoveAll(config, ClientIdKey) | RemoveAll(config, ClientSecretKey) |
                           RemoveAll(config, HasClientSecretKey) | RemoveAll(config, ClearClientSecretKey);
            return stripped ? config.ToString(Formatting.None) : configJson;
        }

        /// <summary>
        /// Whether a pending consent flow still belongs to <paramref name="configJson"/>'s current OAuth client:
        /// same endpoint, and same client id when one is configured. With no configured client id the flow's id
        /// came from Dynamic Client Registration and can't be compared, so only the endpoint is checked. Read
        /// the same case-insensitive, last-wins way as <see cref="MergeOnWrite"/>.
        /// </summary>
        public static bool MatchesOAuthClient(string configJson, string flowClientId, string flowEndpoint)
        {
            if (!TryParse(configJson, out var config))
                return false;

            if (!string.Equals(Normalize(GetString(config, EndpointKey)), Normalize(flowEndpoint), StringComparison.Ordinal))
                return false;

            var configuredClientId = Normalize(GetString(config, ClientIdKey));
            return configuredClientId.Length == 0 ||
                   string.Equals(configuredClientId, Normalize(flowClientId), StringComparison.Ordinal);
        }

        private static bool SameSecret(IMcpTokenProtector protector, string previousStoredSecret, string incomingPlaintext)
        {
            try
            {
                return string.Equals(Resolve(protector, previousStoredSecret), incomingPlaintext, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                // Undecryptable previous value (rotated key / corrupted row): it can't be the same usable secret,
                // so treat the new one as a change and let the caller reset tokens issued under the old one.
                return false;
            }
        }

        private static readonly string[] CanonicalKeys = [EndpointKey, ClientIdKey, ClientSecretKey, AuthTypeKey];

        private static List<JProperty> Variants(JObject config, string key) =>
            config.Properties().Where(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();

        private static bool HasVariant(JObject config, string key) => Variants(config, key).Count > 0;

        // The value McpServerConfig's (case-insensitive) deserialization ends up with: the last occurrence wins.
        private static JToken Effective(JObject config, string key) => Variants(config, key).LastOrDefault()?.Value;

        /// <summary>Collapses every case variant of <paramref name="key"/> into one canonical property holding the
        /// effective value. Returns whether <paramref name="config"/> changed.</summary>
        private static bool Canonicalize(JObject config, string key)
        {
            var variants = Variants(config, key);
            if (variants.Count == 0 || (variants.Count == 1 && variants[0].Name == key))
                return false;

            var effective = variants[^1].Value;
            foreach (var variant in variants)
                variant.Remove();
            config[key] = effective;
            return true;
        }

        private static bool RemoveAll(JObject config, string key)
        {
            var variants = Variants(config, key);
            foreach (var variant in variants)
                variant.Remove();
            return variants.Count > 0;
        }

        private static bool IsMcp(string toolType) => string.Equals(toolType, CommonToolTypeConsts.MCP, StringComparison.Ordinal);

        private static bool IsOAuth2(JObject config)
        {
            var token = Effective(config, AuthTypeKey);
            if (token == null)
                return false;

            return token.Type switch
            {
                JTokenType.String => string.Equals(token.Value<string>(), "OAuth2", StringComparison.OrdinalIgnoreCase),
                // Legacy rows written before McpServerConfig used StringEnumConverter (McpAuthType.OAuth2 == 1).
                JTokenType.Integer => token.Value<long>() == 1,
                _ => false
            };
        }

        private static bool IsTrue(JToken token) => token.Type switch
        {
            JTokenType.Boolean => token.Value<bool>(),
            JTokenType.String => bool.TryParse(token.Value<string>(), out var parsed) && parsed,
            _ => false
        };

        private static string GetString(JObject config, string key) =>
            Effective(config, key) is { Type: JTokenType.String } token ? token.Value<string>() : null;

        private static string Normalize(string value) => value?.Trim() ?? string.Empty;

        private static bool TryParse(string json, out JObject config)
        {
            config = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                config = JsonConvert.DeserializeObject<JObject>(json, ParseSettings);
                return config != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>Outcome of <see cref="McpClientSecretHelper.MergeOnWrite"/>.</summary>
    /// <param name="Config">The config JSON to persist (the input, untouched, when nothing needed changing).</param>
    /// <param name="CredentialsChanged">The client id, the endpoint or the effective client secret changed (including
    /// removal) — existing OAuth tokens for the tool were issued under the old settings and should be deleted.</param>
    /// <param name="IsClientSecretWithoutClientId">A client secret was posted without a client id; the write must be rejected.</param>
    /// <param name="IsInvalidConfig">An MCP config that isn't a JSON object; the write must be rejected.</param>
    public readonly record struct McpClientSecretMergeResult(string Config, bool CredentialsChanged, bool IsClientSecretWithoutClientId, bool IsInvalidConfig = false)
    {
        internal static McpClientSecretMergeResult Unchanged(string config) => new(config, false, false);

        internal static McpClientSecretMergeResult SecretWithoutClientId(string config) => new(config, false, true);

        internal static McpClientSecretMergeResult InvalidConfig(string config) => new(config, false, false, true);
    }
}
