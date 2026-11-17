using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.Common.Models.Config
{
    public class McpServerConfig
    {
        public const string SectionName = "McpServers";

        [Required(ErrorMessage = "Server ID is required")]
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "Server Name is required")]
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Transport Type is required")]
        [JsonProperty("transportType")]
        // wkflo_ui always writes/expects this as a string ("Http", "Stdio"). Without this converter,
        // Newtonsoft's default enum handling serializes the underlying int instead — confirmed directly
        // against a corrupted DB row from StartToolMcpOAuthCommandHandler's RedirectUri-backfill rewrite,
        // which round-trips this config through JsonConvert.SerializeObject. StringEnumConverter still reads
        // both string and legacy int values on deserialize, so this only changes what gets written.
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public TransportType TransportType { get; set; } = TransportType.Stdio;

        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public string[] Arguments { get; set; } = Array.Empty<string>();

        [JsonProperty("environmentVariables")]
        public Dictionary<string, string> EnvironmentVariables = new Dictionary<string, string>();

        [JsonProperty("additionalHeaders")]
        public Dictionary<string, string> AdditionalHeaders = new Dictionary<string, string>();

        [JsonProperty("workingDirectory")]
        public string WorkingDirectory { get; set; } = string.Empty;

        [JsonProperty("endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [JsonProperty("connectionTimeout")]
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        [JsonProperty("maxRetries")]
        public int MaxRetries { get; set; } = 1;

        /// <summary>
        /// Authentication scheme used when connecting to this MCP server. Defaults to <see cref="McpAuthType.None"/>
        /// so existing HTTP/Stdio configs (e.g. a fixed Bearer token via <see cref="AdditionalHeaders"/>) are unaffected.
        /// </summary>
        [JsonProperty("authType")]
        // See TransportType's converter remarks — same reasoning applies here.
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public McpAuthType AuthType { get; set; } = McpAuthType.None;

        /// <summary>
        /// OAuth scopes to request at the authorization endpoint. These take precedence: when set, they are
        /// the scopes asked for, and nothing advertised by the server overrides them. Only when this is empty
        /// does the consent flow fall back to whatever the MCP server advertises for itself via RFC 9728
        /// Protected Resource Metadata (never the authorization server's own RFC 8414 capability list — see
        /// <c>McpOAuthDiscoveryResult.ScopesSupported</c>); if that's empty too, no <c>scope</c> parameter is
        /// sent at all and the provider applies its own default. Configure this explicitly for any provider
        /// where least privilege matters. Only used when <see cref="AuthType"/> is <see cref="McpAuthType.OAuth2"/>.
        /// </summary>
        /// <remarks>
        /// Task 7130 inverted this field's precedence. Previously the ModelContextProtocol SDK drove consent
        /// and preferred server-advertised scopes, making this a fallback; the hand-rolled flow that replaced
        /// it treats an explicit config as authoritative instead.
        /// </remarks>
        [JsonProperty("scopes")]
        public string[] Scopes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Preconfigured OAuth client ID. When empty, the SDK attempts RFC 7591 Dynamic Client Registration.
        /// </summary>
        [JsonProperty("clientId")]
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Preconfigured OAuth client secret. Optional for public clients / PKCE-only flows.
        /// </summary>
        [JsonProperty("clientSecret")]
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// OAuth redirect URI the authorization server sends the browser back to. When left empty, callers
        /// (e.g. the MD API OAuth consent endpoints) compute a default from the current request.
        /// </summary>
        [JsonProperty("redirectUri")]
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// Whether this OAuth2 MCP server's tokens are shared by the whole tenant (<see cref="McpOAuthScope.Shared"/>,
        /// the default — the only mode Humantic supports) or issued one-per-WKFLO-user (<see cref="McpOAuthScope.PerUser"/>
        /// — required for servers with no tenant/org-shared mode at all, e.g. Canva). Fixed and mutually
        /// exclusive per tool: never both for the same connection. Only meaningful when <see cref="AuthType"/>
        /// is <see cref="McpAuthType.OAuth2"/>; see <see cref="Validate"/>.
        /// </summary>
        [JsonProperty("oAuthScope")]
        // See TransportType's converter remarks — same reasoning applies here.
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public McpOAuthScope OAuthScope { get; set; } = McpOAuthScope.Shared;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                throw new InvalidOperationException("MCP Server ID must be configured");

            if (string.IsNullOrWhiteSpace(Name))
                throw new InvalidOperationException("MCP Server Name must be configured");

            if (AuthType == McpAuthType.OAuth2)
            {
                // OAuth2 is only meaningful over HTTP; it never requires a Stdio Command. Enforced, not just
                // documented: without this check, a config with AuthType=OAuth2 but TransportType left at its
                // default (Stdio) passes validation, then McpClientService.CreateTransport switches on
                // TransportType alone and would silently build a Stdio transport, ignoring OAuth entirely.
                if (TransportType != TransportType.Http)
                    throw new InvalidOperationException("OAuth2 authentication requires Http transport");

                if (string.IsNullOrWhiteSpace(Endpoint))
                    throw new InvalidOperationException("Endpoint is required for OAuth2 authentication");
                if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
                    throw new InvalidOperationException("Endpoint must be a valid URI");

                // The ModelContextProtocol SDK's ClientOAuthProvider requires a RedirectUri to even
                // construct the transport (confirmed against the installed SDK), regardless of whether this
                // is an interactive consent attempt or a silent/cached-token reconnect — so it must always be
                // present by the time any caller reaches CreateTransport, not just resolved ad hoc for
                // consent. The MD API's OAuth start endpoint backfills and persists a default the first time
                // a tenant connects; a tool that has never been through consent correctly fails validation
                // here rather than with a confusing SDK-level ArgumentException.
                if (string.IsNullOrWhiteSpace(RedirectUri))
                    throw new InvalidOperationException("RedirectUri is required for OAuth2 authentication");
                if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out _))
                    throw new InvalidOperationException("RedirectUri must be a valid URI");
            }
            else
            {
                // Per-user token storage only makes sense for an OAuth2 server; nothing else ever persists a
                // ToolOAuthToken row to begin with.
                if (OAuthScope == McpOAuthScope.PerUser)
                    throw new InvalidOperationException("OAuthScope = PerUser requires AuthType = OAuth2");

                switch (TransportType)
                {
                    case TransportType.Stdio:
                        if (string.IsNullOrWhiteSpace(Command))
                            throw new InvalidOperationException("Command is required for Stdio transport");
                        break;

                    case TransportType.Http:
                        if (string.IsNullOrWhiteSpace(Endpoint))
                            throw new InvalidOperationException("Endpoint is required for SSE/HTTP transport");
                        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
                            throw new InvalidOperationException("Endpoint must be a valid URI");
                        break;
                }
            }

            if (ConnectionTimeout <= TimeSpan.Zero)
                throw new InvalidOperationException("ConnectionTimeout must be positive");

            if (MaxRetries < 0)
                throw new InvalidOperationException("MaxRetries must be non-negative");
        }

        /// <summary>
        /// Strips the tenant/tool-specific OAuth2 fields — <see cref="ClientId"/>/<see cref="ClientSecret"/>
        /// (a plaintext preconfigured client credential would otherwise leak to every tenant that shares or
        /// installs this tool) and <see cref="RedirectUri"/> (tied to the originating Tool's own callback
        /// URL, meaningless — and actively wrong — for a copy under a different Tool.Id) — forcing a fresh
        /// OAuth consent on the copy. Called when publishing/sharing/installing an OAuth2 MCP tool. Deliberately
        /// leaves <see cref="AuthType"/>, <see cref="Endpoint"/>, and <see cref="Scopes"/> alone: they describe
        /// the MCP server itself, not a secret or a per-tool value.
        /// </summary>
        public void ClearOAuthSecrets()
        {
            ClientId = string.Empty;
            ClientSecret = string.Empty;
            RedirectUri = string.Empty;
        }
    }

    public enum TransportType
    {
        Stdio,
        Http
    }

    public enum McpAuthType
    {
        None,
        OAuth2
    }

    /// <summary>
    /// Which OAuth2 token-storage mode an MCP server uses. See <see cref="McpServerConfig.OAuthScope"/>.
    /// </summary>
    public enum McpOAuthScope
    {
        /// <summary>One token shared by the whole tenant. The default, and the only mode Humantic supports.</summary>
        Shared,

        /// <summary>One token per WKFLO user. Required for servers with no tenant/org-shared mode at all (e.g. Canva).</summary>
        PerUser
    }
}
