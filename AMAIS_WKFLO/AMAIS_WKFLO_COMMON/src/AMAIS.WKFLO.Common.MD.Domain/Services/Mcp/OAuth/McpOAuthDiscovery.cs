using Newtonsoft.Json;
using System.Net;
using System.Text.RegularExpressions;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth
{
    /// <summary>The resolved authorization-server endpoints an MCP OAuth 2.1 consent flow needs.</summary>
    public sealed class McpOAuthDiscoveryResult
    {
        public string AuthorizationEndpoint { get; init; }
        public string TokenEndpoint { get; init; }

        /// <summary>Null when the server has no Dynamic Client Registration (RFC 7591) support — a config
        /// with a blank <c>ClientId</c> against such a server cannot proceed.</summary>
        public string RegistrationEndpoint { get; init; }

        /// <summary>Null when discovery fell all the way through to the MCP-default convention with no
        /// explicit issuer identifier.</summary>
        public string Issuer { get; init; }

        /// <summary>
        /// Scopes advertised for <em>this MCP resource</em> — sourced only from RFC 9728 Protected Resource
        /// Metadata's <c>scopes_supported</c>, never from RFC 8414 Authorization Server Metadata's
        /// same-named field. The two are not interchangeable: RFC 9728's list is the set of scopes used to
        /// request access to this one protected resource (what the MCP authorization spec points at), while
        /// RFC 8414's is the authorization server's entire capability list across every API it fronts — on a
        /// shared tenant (Auth0/Okta/Entra) that can be a large set of unrelated, possibly write/admin
        /// scopes. Using the latter as a default would silently ask users to consent to far more than this
        /// tool needs, so it is deliberately ignored. Null when nothing resource-scoped was advertised.
        /// </summary>
        public string[] ScopesSupported { get; init; }
    }

    /// <summary>
    /// Discovers an MCP server's OAuth 2.1 authorization server, in hand-rolled place of the
    /// ModelContextProtocol SDK's own (internal) equivalent — see <see cref="McpOAuthConsentService"/>'s
    /// remarks for why. Faithfully follows the same discovery order the SDK itself uses: an unauthenticated
    /// probe of the MCP endpoint for a `WWW-Authenticate: resource_metadata=...` hint (or the RFC 9728
    /// well-known default) → RFC 9728 Protected Resource Metadata's first `authorization_servers` entry →
    /// RFC 8414 Authorization Server Metadata (or an OpenID Connect Discovery document, which is a
    /// compatible superset) → the MCP-default `{origin}/authorize`, `/token`, `/register` convention.
    /// </summary>
    public static class McpOAuthDiscovery
    {
        private static readonly Regex ResourceMetadataParameterRegex =
            new("resource_metadata=\"([^\"]+)\"", RegexOptions.Compiled);

        public static async Task<McpOAuthDiscoveryResult> DiscoverAsync(HttpClient httpClient, string mcpEndpoint, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(mcpEndpoint);

            var endpointUri = new Uri(mcpEndpoint, UriKind.Absolute);
            var origin = $"{endpointUri.Scheme}://{endpointUri.Authority}";

            var resourceMetadataUrl =
                await TryGetResourceMetadataUrlFromProbeAsync(httpClient, mcpEndpoint, cancellationToken)
                ?? CombineUri(origin, "/.well-known/oauth-protected-resource");

            var resourceMetadata = await TryGetJsonAsync<ProtectedResourceMetadata>(httpClient, resourceMetadataUrl, cancellationToken);
            var issuer = resourceMetadata?.AuthorizationServers?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            if (!string.IsNullOrWhiteSpace(issuer))
            {
                var authServerMetadata =
                    await TryGetJsonAsync<AuthorizationServerMetadata>(httpClient, CombineUri(issuer, "/.well-known/oauth-authorization-server"), cancellationToken)
                    ?? await TryGetJsonAsync<AuthorizationServerMetadata>(httpClient, CombineUri(issuer, "/.well-known/openid-configuration"), cancellationToken);

                if (authServerMetadata != null &&
                    !string.IsNullOrWhiteSpace(authServerMetadata.AuthorizationEndpoint) &&
                    !string.IsNullOrWhiteSpace(authServerMetadata.TokenEndpoint))
                {
                    return new McpOAuthDiscoveryResult
                    {
                        AuthorizationEndpoint = authServerMetadata.AuthorizationEndpoint,
                        TokenEndpoint = authServerMetadata.TokenEndpoint,
                        RegistrationEndpoint = authServerMetadata.RegistrationEndpoint,
                        Issuer = authServerMetadata.Issuer ?? issuer,
                        // Deliberately ONLY the RFC 9728 protected-resource list, never RFC 8414's
                        // authorization-server list — see McpOAuthDiscoveryResult.ScopesSupported's remarks.
                        ScopesSupported = resourceMetadata?.ScopesSupported
                    };
                }
            }

            // Nothing published (or nothing usable) was discovered — fall back to the MCP-default
            // convention every MCP OAuth2 server this connects to is expected to support at minimum.
            return new McpOAuthDiscoveryResult
            {
                AuthorizationEndpoint = CombineUri(origin, "/authorize"),
                TokenEndpoint = CombineUri(origin, "/token"),
                RegistrationEndpoint = CombineUri(origin, "/register"),
                Issuer = origin,
                ScopesSupported = resourceMetadata?.ScopesSupported
            };
        }

        private static async Task<string> TryGetResourceMetadataUrlFromProbeAsync(HttpClient httpClient, string mcpEndpoint, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await httpClient.GetAsync(mcpEndpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                    return null;

                foreach (var header in response.Headers.WwwAuthenticate)
                {
                    var match = ResourceMetadataParameterRegex.Match(header.Parameter ?? string.Empty);
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
            // Cancellation is not a probe failure: swallowing it here would let a caller that has already
            // given up fall through to several more (equally doomed) discovery requests before anything
            // finally observes the cancellation. Must precede the catch-all below.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Best-effort probe: plenty of MCP servers reject a bare unauthenticated GET outright
                // (network errors, non-401 statuses, no WWW-Authenticate at all) — any failure here just
                // falls through to the RFC 9728 well-known default location instead.
            }

            return null;
        }

        private static string CombineUri(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;

        private static async Task<T> TryGetJsonAsync<T>(HttpClient httpClient, string url, CancellationToken cancellationToken) where T : class
        {
            try
            {
                using var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonConvert.DeserializeObject<T>(json);
            }
            // See TryGetResourceMetadataUrlFromProbeAsync — cancellation must not be treated as a merely
            // absent/malformed optional document. Must precede the catch-all below.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Every metadata document this method fetches is optional at this stage — a missing or
                // malformed one just means "try the next thing in the discovery order" (see DiscoverAsync).
                return null;
            }
        }
    }
}
