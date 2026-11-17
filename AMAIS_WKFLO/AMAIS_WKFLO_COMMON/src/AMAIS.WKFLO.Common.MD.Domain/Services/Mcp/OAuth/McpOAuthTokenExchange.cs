using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth
{
    /// <summary>
    /// The three ways a client may present its <c>client_id</c>/<c>client_secret</c> at the token endpoint
    /// (RFC 6749 §2.3.1, plus the "none" public-client case) — recorded per-flow on
    /// <see cref="Entities.McpOAuthPendingFlow.TokenEndpointAuthMethod"/> from what Dynamic Client
    /// Registration returned (or what a preconfigured <c>McpServerConfig.ClientSecret</c> implies).
    /// </summary>
    public static class McpOAuthTokenEndpointAuthMethods
    {
        public const string ClientSecretBasic = "client_secret_basic";
        public const string ClientSecretPost = "client_secret_post";
        public const string None = "none";
    }

    /// <summary>Authorization-code token exchange (RFC 6749 §4.1.3), performed by
    /// <see cref="McpOAuthConsentService.CompleteAsync"/> against the pending flow's persisted
    /// <c>TokenEndpoint</c>.</summary>
    public static class McpOAuthTokenExchange
    {
        public static async Task<TokenExchangeResponse> ExchangeAuthorizationCodeAsync(
            HttpClient httpClient,
            string tokenEndpoint,
            string code,
            string redirectUri,
            string codeVerifier,
            string clientId,
            string clientSecret,
            string tokenEndpointAuthMethod,
            string resource,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(tokenEndpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            var formValues = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            };

            if (!string.IsNullOrWhiteSpace(resource))
                formValues["resource"] = resource;

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);

            switch (tokenEndpointAuthMethod)
            {
                case McpOAuthTokenEndpointAuthMethods.ClientSecretBasic:
                    // RFC 6749 §2.3.1 requires the client id and secret to each be
                    // application/x-www-form-urlencoded *before* being joined with ':' and base64-encoded —
                    // not passed through raw. Without this, any credential containing a reserved character
                    // (notably '+' or '/', both in the standard base64 alphabet and so common in
                    // DCR-issued secrets, or a ':' in the client id) makes the authorization server decode a
                    // different credential pair than the one it registered, failing with invalid_client.
                    // Uri.EscapeDataString encodes space as %20 rather than '+'; both decode back to a space
                    // server-side, whereas emitting a bare '+' would be misread as one.
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(
                            $"{Uri.EscapeDataString(clientId ?? string.Empty)}:{Uri.EscapeDataString(clientSecret ?? string.Empty)}")));
                    break;

                case McpOAuthTokenEndpointAuthMethods.ClientSecretPost:
                    formValues["client_id"] = clientId;
                    formValues["client_secret"] = clientSecret;
                    break;

                default: // "none" — public client, no secret; client_id travels in the body alone.
                    formValues["client_id"] = clientId;
                    break;
            }

            request.Content = new FormUrlEncodedContent(formValues);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            var tokenResponse = TryDeserialize(responseBody);

            if (!response.IsSuccessStatusCode || tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                var error = tokenResponse?.Error ?? $"HTTP {(int)response.StatusCode}";
                var description = tokenResponse?.ErrorDescription;
                throw new InvalidOperationException(
                    $"MCP OAuth token exchange failed against '{tokenEndpoint}': {error}" +
                    (string.IsNullOrWhiteSpace(description) ? string.Empty : $" — {description}"));
            }

            return tokenResponse;
        }

        private static TokenExchangeResponse TryDeserialize(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<TokenExchangeResponse>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
