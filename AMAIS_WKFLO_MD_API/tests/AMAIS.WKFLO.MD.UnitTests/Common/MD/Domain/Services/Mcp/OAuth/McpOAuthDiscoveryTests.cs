using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp.OAuth
{
    public class McpOAuthDiscoveryTests
    {
        private const string McpEndpoint = "https://mcp.example.com/mcp";

        [Fact]
        public async Task DiscoverAsync_FullChain_ProbeToProtectedResourceToAuthServerMetadata_ResolvesEndpoints()
        {
            var handler = new FakeHttpMessageHandler(request =>
            {
                var url = request.RequestUri.ToString();

                if (url == McpEndpoint)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    response.Headers.TryAddWithoutValidation("WWW-Authenticate",
                        "Bearer resource_metadata=\"https://mcp.example.com/.well-known/oauth-protected-resource\"");
                    return response;
                }

                if (url == "https://mcp.example.com/.well-known/oauth-protected-resource")
                    return JsonResponse("""{"authorization_servers":["https://auth.example.com"],"scopes_supported":["read","write"]}""");

                if (url == "https://auth.example.com/.well-known/oauth-authorization-server")
                    return JsonResponse("""
                        {
                            "issuer": "https://auth.example.com",
                            "authorization_endpoint": "https://auth.example.com/authorize",
                            "token_endpoint": "https://auth.example.com/token",
                            "registration_endpoint": "https://auth.example.com/register"
                        }
                        """);

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var result = await McpOAuthDiscovery.DiscoverAsync(new HttpClient(handler), McpEndpoint, TestContext.Current.CancellationToken);

            Assert.Equal("https://auth.example.com/authorize", result.AuthorizationEndpoint);
            Assert.Equal("https://auth.example.com/token", result.TokenEndpoint);
            Assert.Equal("https://auth.example.com/register", result.RegistrationEndpoint);
            Assert.Equal("https://auth.example.com", result.Issuer);
            Assert.Equal(["read", "write"], result.ScopesSupported);
        }

        [Fact]
        public async Task DiscoverAsync_OAuthAuthorizationServerMetadataMissing_FallsBackToOpenIdConfiguration()
        {
            var handler = new FakeHttpMessageHandler(request =>
            {
                var url = request.RequestUri.ToString();

                if (url == McpEndpoint)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    response.Headers.TryAddWithoutValidation("WWW-Authenticate",
                        "Bearer resource_metadata=\"https://mcp.example.com/.well-known/oauth-protected-resource\"");
                    return response;
                }

                if (url == "https://mcp.example.com/.well-known/oauth-protected-resource")
                    return JsonResponse("""{"authorization_servers":["https://auth.example.com"]}""");

                if (url == "https://auth.example.com/.well-known/oauth-authorization-server")
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                if (url == "https://auth.example.com/.well-known/openid-configuration")
                    return JsonResponse("""
                        {
                            "issuer": "https://auth.example.com",
                            "authorization_endpoint": "https://auth.example.com/oidc/authorize",
                            "token_endpoint": "https://auth.example.com/oidc/token"
                        }
                        """);

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var result = await McpOAuthDiscovery.DiscoverAsync(new HttpClient(handler), McpEndpoint, TestContext.Current.CancellationToken);

            Assert.Equal("https://auth.example.com/oidc/authorize", result.AuthorizationEndpoint);
            Assert.Equal("https://auth.example.com/oidc/token", result.TokenEndpoint);
            // The openid-configuration document in this test never advertised one.
            Assert.Null(result.RegistrationEndpoint);
        }

        [Fact]
        public async Task DiscoverAsync_NoWwwAuthenticateHeader_TriesWellKnownProtectedResourceLocation()
        {
            var handler = new FakeHttpMessageHandler(request =>
            {
                var url = request.RequestUri.ToString();

                // A plain 200 (or anything other than 401) — no WWW-Authenticate hint to follow.
                if (url == McpEndpoint)
                    return new HttpResponseMessage(HttpStatusCode.OK);

                if (url == "https://mcp.example.com/.well-known/oauth-protected-resource")
                    return JsonResponse("""{"authorization_servers":["https://auth.example.com"]}""");

                if (url == "https://auth.example.com/.well-known/oauth-authorization-server")
                    return JsonResponse("""
                        {
                            "authorization_endpoint": "https://auth.example.com/authorize",
                            "token_endpoint": "https://auth.example.com/token"
                        }
                        """);

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var result = await McpOAuthDiscovery.DiscoverAsync(new HttpClient(handler), McpEndpoint, TestContext.Current.CancellationToken);

            Assert.Equal("https://auth.example.com/authorize", result.AuthorizationEndpoint);
        }

        [Fact]
        public async Task DiscoverAsync_NothingPublished_FallsBackToMcpDefaultConvention()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            var result = await McpOAuthDiscovery.DiscoverAsync(new HttpClient(handler), McpEndpoint, TestContext.Current.CancellationToken);

            Assert.Equal("https://mcp.example.com/authorize", result.AuthorizationEndpoint);
            Assert.Equal("https://mcp.example.com/token", result.TokenEndpoint);
            Assert.Equal("https://mcp.example.com/register", result.RegistrationEndpoint);
            Assert.Equal("https://mcp.example.com", result.Issuer);
        }

        [Fact]
        public async Task DiscoverAsync_ProtectedResourceHasNoAuthorizationServers_FallsBackToMcpDefaultConvention()
        {
            var handler = new FakeHttpMessageHandler(request =>
            {
                var url = request.RequestUri.ToString();

                if (url == McpEndpoint)
                    return new HttpResponseMessage(HttpStatusCode.OK);

                if (url == "https://mcp.example.com/.well-known/oauth-protected-resource")
                    return JsonResponse("""{"authorization_servers":[]}""");

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var result = await McpOAuthDiscovery.DiscoverAsync(new HttpClient(handler), McpEndpoint, TestContext.Current.CancellationToken);

            Assert.Equal("https://mcp.example.com/authorize", result.AuthorizationEndpoint);
            Assert.Equal("https://mcp.example.com", result.Issuer);
        }

        // Copilot PR review (PR 16245): the two `scopes_supported` fields are NOT interchangeable. RFC 9728's
        // (protected-resource metadata) is the scope list for this one MCP resource; RFC 8414's
        // (authorization-server metadata) is the AS's whole capability list across every API it fronts, which
        // on a shared tenant can include unrelated write/admin scopes. Defaulting to the latter would silently
        // ask users to consent to far more than the tool needs, so only the RFC 9728 list is ever surfaced.
        [Fact]
        public async Task DiscoverAsync_BothMetadataDocumentsAdvertiseScopes_UsesOnlyTheProtectedResourceList()
        {
            var handler = new FakeHttpMessageHandler(request =>
            {
                var url = request.RequestUri.ToString();

                if (url == McpEndpoint)
                    return new HttpResponseMessage(HttpStatusCode.OK);

                if (url == "https://mcp.example.com/.well-known/oauth-protected-resource")
                    return JsonResponse("""{"authorization_servers":["https://auth.example.com"],"scopes_supported":["mcp:read"]}""");

                if (url == "https://auth.example.com/.well-known/oauth-authorization-server")
                    return JsonResponse("""
                        {
                            "issuer": "https://auth.example.com",
                            "authorization_endpoint": "https://auth.example.com/authorize",
                            "token_endpoint": "https://auth.example.com/token",
                            "scopes_supported": ["mcp:read", "unrelated-api:write", "tenant:admin"]
                        }
                        """);

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var result = await McpOAuthDiscovery.DiscoverAsync(new HttpClient(handler), McpEndpoint, TestContext.Current.CancellationToken);

            Assert.Equal(["mcp:read"], result.ScopesSupported);
        }

        // Copilot PR review (PR 16245): discovery's best-effort catches must not swallow cancellation — doing
        // so let an already-abandoned BeginAsync fall through to several more doomed discovery requests before
        // anything observed the cancellation.
        [Fact]
        public async Task DiscoverAsync_CancelledMidChain_PropagatesInsteadOfContinuingToRemainingUrls()
        {
            using var cts = new System.Threading.CancellationTokenSource();

            var handler = new FakeHttpMessageHandler(request =>
            {
                if (request.RequestUri.ToString() == McpEndpoint)
                {
                    var probeResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    probeResponse.Headers.TryAddWithoutValidation("WWW-Authenticate",
                        "Bearer resource_metadata=\"https://mcp.example.com/.well-known/oauth-protected-resource\"");
                    return probeResponse;
                }

                // The caller gives up while the protected-resource document is in flight — exactly what a
                // real cancelled HttpClient call surfaces.
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => McpOAuthDiscovery.DiscoverAsync(new HttpClient(handler), McpEndpoint, cts.Token));

            // Only the probe and the cancelled protected-resource fetch. Before this fix the cancellation was
            // swallowed as "optional document missing" and discovery carried on to both authorization-server
            // metadata URLs and then the MCP-default fallback.
            Assert.Equal(2, handler.Requests.Count);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
