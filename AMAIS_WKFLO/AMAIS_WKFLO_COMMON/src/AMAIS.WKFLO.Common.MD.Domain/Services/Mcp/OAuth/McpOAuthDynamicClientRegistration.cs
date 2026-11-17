using Newtonsoft.Json;
using System.Text;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth
{
    public sealed class McpOAuthClientRegistration
    {
        public string ClientId { get; init; }
        public string ClientSecret { get; init; }
        public string TokenEndpointAuthMethod { get; init; }
    }

    /// <summary>
    /// RFC 7591 Dynamic Client Registration, performed once (per <see cref="McpOAuthConsentService.BeginAsync"/>
    /// call) whenever the tool has no <c>McpServerConfig.ClientId</c> preconfigured.
    /// </summary>
    public static class McpOAuthDynamicClientRegistration
    {
        public static async Task<McpOAuthClientRegistration> RegisterAsync(
            HttpClient httpClient,
            string registrationEndpoint,
            string redirectUri,
            string clientName,
            string scope,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(registrationEndpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

            var requestBody = new
            {
                redirect_uris = new[] { redirectUri },
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
                token_endpoint_auth_method = "client_secret_post",
                client_name = clientName,
                scope
            };

            using var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(registrationEndpoint, content, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Dynamic Client Registration failed against '{registrationEndpoint}' ({(int)response.StatusCode}): {responseBody}");

            var registration = JsonConvert.DeserializeObject<DynamicClientRegistrationResponse>(responseBody);
            if (registration == null || string.IsNullOrWhiteSpace(registration.ClientId))
                throw new InvalidOperationException($"Dynamic Client Registration against '{registrationEndpoint}' did not return a client_id.");

            return new McpOAuthClientRegistration
            {
                ClientId = registration.ClientId,
                ClientSecret = registration.ClientSecret,
                // The server's own auth method takes priority if it told us one; otherwise assume the same
                // client_secret_post we requested when it issued a secret, or "none" for a public client.
                TokenEndpointAuthMethod = !string.IsNullOrWhiteSpace(registration.TokenEndpointAuthMethod)
                    ? registration.TokenEndpointAuthMethod
                    : (!string.IsNullOrWhiteSpace(registration.ClientSecret) ? "client_secret_post" : "none")
            };
        }
    }
}
