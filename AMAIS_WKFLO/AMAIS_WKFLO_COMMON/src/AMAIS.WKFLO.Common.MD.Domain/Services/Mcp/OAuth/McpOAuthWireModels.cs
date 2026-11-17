using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth
{
    /// <summary>RFC 9728 OAuth 2.0 Protected Resource Metadata, as published by the MCP server itself
    /// (typically at <c>/.well-known/oauth-protected-resource</c>).</summary>
    public sealed class ProtectedResourceMetadata
    {
        [JsonProperty("authorization_servers")]
        public string[] AuthorizationServers { get; set; }

        [JsonProperty("scopes_supported")]
        public string[] ScopesSupported { get; set; }
    }

    /// <summary>RFC 8414 OAuth 2.0 Authorization Server Metadata (also compatible with an OpenID Connect
    /// Discovery document, which is a superset of the same fields this type reads).</summary>
    public sealed class AuthorizationServerMetadata
    {
        [JsonProperty("issuer")]
        public string Issuer { get; set; }

        [JsonProperty("authorization_endpoint")]
        public string AuthorizationEndpoint { get; set; }

        [JsonProperty("token_endpoint")]
        public string TokenEndpoint { get; set; }

        [JsonProperty("registration_endpoint")]
        public string RegistrationEndpoint { get; set; }

        [JsonProperty("scopes_supported")]
        public string[] ScopesSupported { get; set; }
    }

    /// <summary>RFC 7591 Dynamic Client Registration response.</summary>
    public sealed class DynamicClientRegistrationResponse
    {
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("client_secret")]
        public string ClientSecret { get; set; }

        [JsonProperty("token_endpoint_auth_method")]
        public string TokenEndpointAuthMethod { get; set; }
    }

    /// <summary>RFC 6749 §5.1 access token response (successful token endpoint reply).</summary>
    public sealed class TokenExchangeResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }

        /// <summary>RFC 6749 §5.2 error response's <c>error</c> field; null on success.</summary>
        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("error_description")]
        public string ErrorDescription { get; set; }
    }
}
