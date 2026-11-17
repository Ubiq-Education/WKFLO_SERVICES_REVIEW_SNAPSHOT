using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp.OAuth;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// Exercises <see cref="McpOAuthConsentService"/>'s hand-rolled OAuth 2.1 flow — discovery/DCR/PKCE and
    /// pending-flow persistence in <c>BeginAsync</c>, and state lookup/expiry/CSRF-rejection plus token
    /// exchange/save/row-deletion in <c>CompleteAsync</c> — against a fake outbound <see cref="HttpClient"/>
    /// and a mocked <see cref="IMasterDataDbContext"/>.
    /// </summary>
    /// <remarks>
    /// <c>CompleteAsync</c>'s final step (<c>VerifyMcpHandshakeAsync</c>) makes a real MCP handshake attempt
    /// via the ModelContextProtocol SDK against <c>McpOAuthPendingFlow.Resource</c> — that's the point of the
    /// check (it's what previously caught e.g. Humantic's "no active subscription" business error). Tests
    /// below that reach this step deliberately point <c>Resource</c> at an address nothing listens on
    /// (<see cref="UnreachableMcpEndpoint"/>) so the handshake fails fast and deterministically; proving a
    /// *successful* handshake would require a real (or locally-hosted) MCP server and is exercised instead by
    /// PLAN.md's manual verification against Humantic.ai.
    /// </remarks>
    public class McpOAuthConsentServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        private static readonly Guid ConnectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid RequestingUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // Port 1 on loopback: nothing ever listens there, so a connection attempt fails immediately instead
        // of timing out — see the class remarks.
        private const string UnreachableMcpEndpoint = "http://127.0.0.1:1/mcp";

        private static McpServerConfig BuildOAuthConfig(string clientId = "preconfigured-client", string clientSecret = "preconfigured-secret", string endpoint = UnreachableMcpEndpoint) => new()
        {
            Id = "test-server",
            Name = "Test MCP Server",
            TransportType = TransportType.Http,
            Endpoint = endpoint,
            AuthType = McpAuthType.OAuth2,
            Scopes = ["read", "write"],
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback"
        };

        private sealed class Fixture
        {
            public McpOAuthConsentService Service { get; init; }
            public Mock<IMasterDataDbContext> Db { get; init; }
            public Mock<Microsoft.EntityFrameworkCore.DbSet<McpOAuthPendingFlow>> FlowsDbSet { get; init; }
            public Mock<IMcpOAuthTokenStore> TokenStore { get; init; }
        }

        // Default tool on ConnectionId: same endpoint as the test flows, no preconfigured client id (so any
        // flow client id matches) — CompleteAsync only saves a token while a tool still uses the flow's client.
        private static readonly string DefaultToolConfig = $"{{\"authType\":\"OAuth2\",\"endpoint\":\"{UnreachableMcpEndpoint}\"}}";

        private static Fixture BuildFixture(Func<HttpRequestMessage, HttpResponseMessage> responder, List<McpOAuthPendingFlow> seedFlows = null,
            List<TenantTool> tenantTools = null, List<PublishedTenantTool> publishedTools = null)
        {
            var flows = seedFlows ?? [];
            var flowsDbSet = flows.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.McpOAuthPendingFlows).Returns(flowsDbSet.Object);
            db.Setup(x => x.TenantTools).Returns((tenantTools ?? [new TenantTool { Id = 1, McpOAuthConnectionId = ConnectionId, Config = DefaultToolConfig }]).BuildMockDbSet().Object);
            db.Setup(x => x.PublishedTenantTools).Returns((publishedTools ?? []).BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var tokenStore = new Mock<IMcpOAuthTokenStore>();

            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            var protector = new Mock<IMcpTokenProtector>();
            // Identity pass-through — these tests aren't about the encryption itself (see
            // AesMcpTokenProtectorTests for that), just that Protect/Unprotect are actually called.
            protector.Setup(x => x.Protect(It.IsAny<string>())).Returns((string s) => s);
            protector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string s) => s);

            var httpClient = new HttpClient(new FakeHttpMessageHandler(responder));

            var service = new McpOAuthConsentService(
                httpClient, db.Object, tokenStore.Object, protector.Object, dateTimeProvider.Object,
                Mock.Of<ILogger<McpOAuthConsentService>>());

            return new Fixture { Service = service, Db = db, FlowsDbSet = flowsDbSet, TokenStore = tokenStore };
        }

        // Always 404s discovery's well-known lookups so DiscoverAsync falls straight to the MCP-default
        // convention ({origin}/authorize, /token, /register) — the simplest deterministic discovery outcome
        // for tests that aren't specifically about discovery itself (McpOAuthDiscoveryTests covers that).
        private static HttpResponseMessage NotFound(HttpRequestMessage request) => new(HttpStatusCode.NotFound);

        // --- BeginAsync ---------------------------------------------------------------------------------

        [Fact]
        public async Task BeginAsync_AuthTypeNotOAuth2_Throws()
        {
            var fixture = BuildFixture(NotFound);
            var config = BuildOAuthConfig();
            config.AuthType = McpAuthType.None;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BeginAsync_BlankRedirectUri_Throws()
        {
            var fixture = BuildFixture(NotFound);
            var config = BuildOAuthConfig();
            config.RedirectUri = "";

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BeginAsync_PreconfiguredClientId_PersistsPendingFlowAndReturnsAuthorizationUri()
        {
            var fixture = BuildFixture(NotFound);
            var config = BuildOAuthConfig();

            var authorizationUri = await fixture.Service.BeginAsync(config, ConnectionId, RequestingUserId, TestContext.Current.CancellationToken);

            fixture.FlowsDbSet.Verify(x => x.Add(It.Is<McpOAuthPendingFlow>(f =>
                f.ConnectionId == ConnectionId &&
                f.RequestingUserId == RequestingUserId &&
                f.ClientId == "preconfigured-client" &&
                f.TokenEndpointAuthMethod == "client_secret_post" &&
                f.TokenEndpoint == "http://127.0.0.1:1/token" &&
                f.RedirectUri == config.RedirectUri &&
                f.Resource == config.Endpoint &&
                f.Scope == "read write" &&
                f.CreatedOnUtc == Now &&
                f.ExpiresOnUtc == Now.AddMinutes(10) &&
                !string.IsNullOrEmpty(f.State) &&
                !string.IsNullOrEmpty(f.CodeVerifier))),
                Times.Once);
            fixture.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

            var query = System.Web.HttpUtility.ParseQueryString(authorizationUri.Query);
            Assert.Equal("http://127.0.0.1:1/authorize", authorizationUri.GetLeftPart(UriPartial.Path));
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("preconfigured-client", query["client_id"]);
            Assert.Equal(config.RedirectUri, query["redirect_uri"]);
            Assert.Equal("read write", query["scope"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            Assert.False(string.IsNullOrEmpty(query["state"]));
            Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
            Assert.Equal(config.Endpoint, query["resource"]);
        }

        // US 7849: the tool config stores the preconfigured secret as "enc:v1:" + IMcpTokenProtector output.
        // The identity protector above makes "enc:v1:plain" decrypt to "plain", so a pending flow carrying
        // "plain" proves the prefix was recognised and the value decrypted (not forwarded as ciphertext).
        [Fact]
        public async Task BeginAsync_EncryptedPreconfiguredClientSecret_IsDecryptedBeforeUse()
        {
            var fixture = BuildFixture(NotFound);
            var config = BuildOAuthConfig(clientSecret: McpClientSecretHelper.EncryptedPrefix + "decrypted-secret");

            await fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken);

            fixture.FlowsDbSet.Verify(x => x.Add(It.Is<McpOAuthPendingFlow>(f =>
                f.ClientSecret == "decrypted-secret" && f.TokenEndpointAuthMethod == "client_secret_post")),
                Times.Once);
        }

        [Fact]
        public async Task BeginAsync_LegacyPlaintextPreconfiguredClientSecret_PassesThrough()
        {
            var fixture = BuildFixture(NotFound);
            var config = BuildOAuthConfig(clientSecret: "legacy-plain");

            await fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken);

            fixture.FlowsDbSet.Verify(x => x.Add(It.Is<McpOAuthPendingFlow>(f =>
                f.ClientSecret == "legacy-plain" && f.TokenEndpointAuthMethod == "client_secret_post")),
                Times.Once);
        }

        [Fact]
        public async Task BeginAsync_PreconfiguredClientIdWithoutSecret_UsesPublicClientAuth()
        {
            var fixture = BuildFixture(NotFound);
            var config = BuildOAuthConfig(clientSecret: "");

            await fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken);

            fixture.FlowsDbSet.Verify(x => x.Add(It.Is<McpOAuthPendingFlow>(f =>
                f.ClientId == "preconfigured-client" && f.TokenEndpointAuthMethod == "none")),
                Times.Once);
        }

        [Fact]
        public async Task BeginAsync_NoPreconfiguredClientId_PerformsDynamicClientRegistration()
        {
            var fixture = BuildFixture(request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri.ToString() == "http://127.0.0.1:1/register")
                    return new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent("""{"client_id":"dcr-client","client_secret":"dcr-secret","token_endpoint_auth_method":"client_secret_post"}""")
                    };

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var config = BuildOAuthConfig(clientId: "", clientSecret: "");

            await fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken);

            fixture.FlowsDbSet.Verify(x => x.Add(It.Is<McpOAuthPendingFlow>(f =>
                f.ClientId == "dcr-client" && f.TokenEndpointAuthMethod == "client_secret_post")),
                Times.Once);
        }

        [Fact]
        public async Task BeginAsync_NoPreconfiguredClientId_NoRegistrationEndpointDiscovered_Throws()
        {
            // A discovered authorization server that publishes authorization_endpoint/token_endpoint but no
            // registration_endpoint — unlike the MCP-default fallback (which always assumes one), this
            // authorization server genuinely doesn't support Dynamic Client Registration.
            var fixture = BuildFixture(request =>
            {
                var url = request.RequestUri.ToString();

                if (url == BuildOAuthConfig().Endpoint)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    response.Headers.TryAddWithoutValidation("WWW-Authenticate",
                        "Bearer resource_metadata=\"http://127.0.0.1:1/.well-known/oauth-protected-resource\"");
                    return response;
                }

                if (url == "http://127.0.0.1:1/.well-known/oauth-protected-resource")
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"authorization_servers":["https://auth.example.com"]}""")
                    };

                if (url == "https://auth.example.com/.well-known/oauth-authorization-server")
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"authorization_endpoint":"https://auth.example.com/authorize","token_endpoint":"https://auth.example.com/token"}""")
                    };

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var config = BuildOAuthConfig(clientId: "", clientSecret: "");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BeginAsync_ExpiredPendingFlowsExist_OpportunisticallyDeletesOnlyThose()
        {
            var expiredFlow = new McpOAuthPendingFlow { State = "expired-state", ExpiresOnUtc = Now.AddMinutes(-1) };
            var validFlow = new McpOAuthPendingFlow { State = "still-valid-state", ExpiresOnUtc = Now.AddMinutes(5) };
            var fixture = BuildFixture(NotFound, [expiredFlow, validFlow]);

            await fixture.Service.BeginAsync(BuildOAuthConfig(), ConnectionId, null, TestContext.Current.CancellationToken);

            var flowsDbSetMock = fixture.FlowsDbSet;
            flowsDbSetMock.Verify(x => x.Remove(expiredFlow), Times.Once);
            flowsDbSetMock.Verify(x => x.Remove(validFlow), Times.Never);
        }

        // Regression test: BeginAsync previously computed the DCR request's scope and the authorize
        // URL/persisted flow's scope from two different expressions — DCR used only the config's own
        // Scopes with no fallback, while the authorize URL (and the persisted row) also fell back to the
        // server's resource-advertised scopes when the config had none. That let a client be registered for
        // one scope set and then asked to authorize a different one. Both must now agree.
        [Fact]
        public async Task BeginAsync_ConfigHasNoScopes_RegistersClientWithTheSameFallbackScopeItLaterAuthorizesFor()
        {
            HttpRequestMessage dcrRequest = null;
            string dcrRequestBody = null;
            var fixture = BuildFixture(request =>
            {
                var url = request.RequestUri.ToString();

                if (url == BuildOAuthConfig().Endpoint)
                    return new HttpResponseMessage(HttpStatusCode.OK);

                if (url == "http://127.0.0.1:1/.well-known/oauth-protected-resource")
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"authorization_servers":["https://auth.example.com"],"scopes_supported":["resource-a","resource-b"]}""")
                    };

                if (url == "https://auth.example.com/.well-known/oauth-authorization-server")
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"authorization_endpoint":"https://auth.example.com/authorize","token_endpoint":"https://auth.example.com/token","registration_endpoint":"https://auth.example.com/register"}""")
                    };

                if (request.Method == HttpMethod.Post && url == "https://auth.example.com/register")
                {
                    dcrRequest = request;
                    dcrRequestBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent("""{"client_id":"dcr-client","client_secret":"dcr-secret","token_endpoint_auth_method":"client_secret_post"}""")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var config = BuildOAuthConfig(clientId: "", clientSecret: "");
            config.Scopes = [];

            var authorizationUri = await fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken);

            Assert.NotNull(dcrRequest);
            Assert.Contains("\"scope\":\"resource-a resource-b\"", dcrRequestBody);

            var query = System.Web.HttpUtility.ParseQueryString(authorizationUri.Query);
            Assert.Equal("resource-a resource-b", query["scope"]);

            fixture.FlowsDbSet.Verify(x => x.Add(It.Is<McpOAuthPendingFlow>(f =>
                f.Scope == "resource-a resource-b")),
                Times.Once);
        }

        [Fact]
        public async Task BeginAsync_ConfigHasNoScopes_FallsBackToDiscoveredScopesSupported()
        {
            var fixture = BuildFixture(request =>
            {
                var url = request.RequestUri.ToString();

                if (url == UnreachableMcpEndpoint)
                    return new HttpResponseMessage(HttpStatusCode.OK);

                if (url == "http://127.0.0.1:1/.well-known/oauth-protected-resource")
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"authorization_servers":["https://auth.example.com"],"scopes_supported":["fallback-a","fallback-b"]}""")
                    };

                if (url == "https://auth.example.com/.well-known/oauth-authorization-server")
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"authorization_endpoint":"https://auth.example.com/authorize","token_endpoint":"https://auth.example.com/token"}""")
                    };

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var config = BuildOAuthConfig();
            config.Scopes = [];

            await fixture.Service.BeginAsync(config, ConnectionId, null, TestContext.Current.CancellationToken);

            fixture.FlowsDbSet.Verify(x => x.Add(It.Is<McpOAuthPendingFlow>(f =>
                f.Scope == "fallback-a fallback-b")),
                Times.Once);
        }

        // --- CompleteAsync -------------------------------------------------------------------------------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task CompleteAsync_NoState_ReturnsFalse(string state)
        {
            var fixture = BuildFixture(NotFound);

            Assert.False(await fixture.Service.CompleteAsync("code", state, "iss", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task CompleteAsync_UnknownState_ReturnsFalse()
        {
            var fixture = BuildFixture(NotFound);

            Assert.False(await fixture.Service.CompleteAsync("code", "never-registered-state", "iss", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task CompleteAsync_ExpiredFlow_TreatedAsMissing_ReturnsFalse()
        {
            var expiredFlow = new McpOAuthPendingFlow
            {
                State = "expired-state",
                ConnectionId = ConnectionId,
                ExpiresOnUtc = Now.AddSeconds(-1)
            };
            var fixture = BuildFixture(NotFound, [expiredFlow]);

            Assert.False(await fixture.Service.CompleteAsync("code", "expired-state", null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task CompleteAsync_IssuerMismatch_ReturnsFalseAndConsumesFlow()
        {
            var flow = new McpOAuthPendingFlow
            {
                State = "state-abc",
                ConnectionId = ConnectionId,
                ExpiresOnUtc = Now.AddMinutes(5),
                AuthorizationServerIssuer = "https://auth.example.com"
            };
            var fixture = BuildFixture(NotFound, [flow]);

            var completed = await fixture.Service.CompleteAsync("code", "state-abc", "https://evil.example.com", TestContext.Current.CancellationToken);

            Assert.False(completed);
            fixture.FlowsDbSet.Verify(x => x.Remove(flow), Times.Once);
        }

        [Fact]
        public async Task CompleteAsync_NoCode_ReturnsFalseAndConsumesFlow_WithoutExchangingAnyToken()
        {
            var flow = new McpOAuthPendingFlow
            {
                State = "state-abc",
                ConnectionId = ConnectionId,
                ExpiresOnUtc = Now.AddMinutes(5),
                TokenEndpoint = "http://127.0.0.1:1/token"
            };
            var fixture = BuildFixture(request => throw new InvalidOperationException($"Unexpected HTTP call to {request.RequestUri}"), [flow]);

            var completed = await fixture.Service.CompleteAsync(null, "state-abc", null, TestContext.Current.CancellationToken);

            Assert.False(completed);
            fixture.FlowsDbSet.Verify(x => x.Remove(flow), Times.Once);
        }

        [Fact]
        public async Task CompleteAsync_TokenExchangeFails_RecordsFailureReturnsFalseAndConsumesFlow()
        {
            var flow = new McpOAuthPendingFlow
            {
                State = "state-abc",
                ConnectionId = ConnectionId,
                RequestingUserId = RequestingUserId,
                ExpiresOnUtc = Now.AddMinutes(5),
                TokenEndpoint = "http://127.0.0.1:1/token",
                ClientId = "client-id",
                ClientSecret = "client-secret",
                TokenEndpointAuthMethod = "client_secret_post",
                CodeVerifier = "verifier",
                RedirectUri = "https://wkflo.example.com/callback",
                Resource = UnreachableMcpEndpoint
            };
            var fixture = BuildFixture(request => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"invalid_grant"}""")
            }, [flow]);

            var completed = await fixture.Service.CompleteAsync("bad-code", "state-abc", null, TestContext.Current.CancellationToken);

            Assert.False(completed);
            fixture.TokenStore.Verify(x => x.SaveAsync(It.IsAny<Guid>(), It.IsAny<McpOAuthTokenData>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            fixture.TokenStore.Verify(x => x.RecordConnectionResultAsync(ConnectionId, false, It.Is<string>(e => e.Contains("invalid_grant")), RequestingUserId, It.IsAny<CancellationToken>()), Times.Once);
            fixture.FlowsDbSet.Verify(x => x.Remove(flow), Times.Once);
        }

        // --- PR review: tool settings changed while consent was in flight --------------------------------

        private static McpOAuthPendingFlow InFlightFlow() => new()
        {
            State = "state-race",
            ConnectionId = ConnectionId,
            ExpiresOnUtc = Now.AddMinutes(5),
            TokenEndpoint = "http://127.0.0.1:1/token",
            ClientId = "client-id",
            ClientSecret = "",
            TokenEndpointAuthMethod = "none",
            CodeVerifier = "verifier",
            RedirectUri = "https://wkflo.example.com/callback",
            Resource = UnreachableMcpEndpoint
        };

        private static HttpResponseMessage TokenOk(HttpRequestMessage _) => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"at\",\"token_type\":\"Bearer\"}")
        };

        private static TenantTool ToolWith(string clientId, string endpoint) => new()
        {
            Id = 1,
            McpOAuthConnectionId = ConnectionId,
            Config = $"{{\"authType\":\"OAuth2\",\"endpoint\":\"{endpoint}\",\"ClientId\":\"{clientId}\"}}"
        };

        [Theory]
        [InlineData("another-client", UnreachableMcpEndpoint)]
        [InlineData("client-id", "https://other.example.com/mcp")]
        public async Task CompleteAsync_ToolClientIdOrEndpointChangedMidFlow_DiscardsTokenAndConsumesFlow(string currentClientId, string currentEndpoint)
        {
            var flow = InFlightFlow();
            var fixture = BuildFixture(TokenOk, [flow], tenantTools: [ToolWith(currentClientId, currentEndpoint)]);

            var completed = await fixture.Service.CompleteAsync("code", flow.State, null, TestContext.Current.CancellationToken);

            Assert.False(completed);
            fixture.TokenStore.Verify(x => x.SaveAsync(It.IsAny<Guid>(), It.IsAny<McpOAuthTokenData>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            fixture.FlowsDbSet.Verify(x => x.Remove(flow), Times.Once);
        }

        [Fact]
        public async Task CompleteAsync_ToolDeletedMidFlow_DiscardsToken()
        {
            var flow = InFlightFlow();
            var fixture = BuildFixture(TokenOk, [flow], tenantTools: []);

            Assert.False(await fixture.Service.CompleteAsync("code", flow.State, null, TestContext.Current.CancellationToken));
            fixture.TokenStore.Verify(x => x.SaveAsync(It.IsAny<Guid>(), It.IsAny<McpOAuthTokenData>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CompleteAsync_ToolStillMatches_CaseVariantKeys_SavesToken()
        {
            var flow = InFlightFlow();
            var fixture = BuildFixture(TokenOk, [flow], tenantTools: [ToolWith("client-id", UnreachableMcpEndpoint)]);

            await fixture.Service.CompleteAsync("code", flow.State, null, TestContext.Current.CancellationToken);

            fixture.TokenStore.Verify(x => x.SaveAsync(ConnectionId, It.IsAny<McpOAuthTokenData>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CompleteAsync_OnlyPublishedCopyStillMatches_SavesToken()
        {
            // The tenant draft's client id changed but the published copy (same connection id, can start consent
            // from the public widget) still uses the flow's client until republished.
            var flow = InFlightFlow();
            var published = new PublishedTenantTool { Id = 2, McpOAuthConnectionId = ConnectionId, Config = ToolWith("client-id", UnreachableMcpEndpoint).Config };
            var fixture = BuildFixture(TokenOk, [flow], tenantTools: [ToolWith("new-client", UnreachableMcpEndpoint)], publishedTools: [published]);

            await fixture.Service.CompleteAsync("code", flow.State, null, TestContext.Current.CancellationToken);

            fixture.TokenStore.Verify(x => x.SaveAsync(ConnectionId, It.IsAny<McpOAuthTokenData>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CompleteAsync_TokenExchangeSucceeds_SavesToken_ThenHandshakeFails_RecordsFailureAndConsumesFlow()
        {
            var flow = new McpOAuthPendingFlow
            {
                State = "state-abc",
                ConnectionId = ConnectionId,
                RequestingUserId = RequestingUserId,
                ExpiresOnUtc = Now.AddMinutes(5),
                TokenEndpoint = "http://127.0.0.1:1/token",
                ClientId = "client-id",
                ClientSecret = "client-secret",
                TokenEndpointAuthMethod = "client_secret_post",
                CodeVerifier = "verifier",
                RedirectUri = "https://wkflo.example.com/callback",
                Scope = "read",
                AuthorizationServerIssuer = "https://auth.example.com",
                // Nothing listens here — the handshake verification step is expected to fail; see the class remarks.
                Resource = UnreachableMcpEndpoint
            };
            var fixture = BuildFixture(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"new-access-token","token_type":"Bearer","expires_in":3600,"refresh_token":"new-refresh-token","scope":"read"}""")
            }, [flow]);

            var completed = await fixture.Service.CompleteAsync("good-code", "state-abc", "https://auth.example.com", TestContext.Current.CancellationToken);

            // Token exchange succeeded and was saved — this is real, independent of the (expected-to-fail)
            // MCP handshake that follows it.
            fixture.TokenStore.Verify(x => x.SaveAsync(
                ConnectionId,
                It.Is<McpOAuthTokenData>(d =>
                    d.AccessToken == "new-access-token" &&
                    d.TokenType == "Bearer" &&
                    d.RefreshToken == "new-refresh-token" &&
                    d.ExpiresIn == TimeSpan.FromSeconds(3600) &&
                    d.ClientId == "client-id" &&
                    d.ClientSecret == "client-secret" &&
                    d.TokenEndpointAuthMethod == "client_secret_post" &&
                    d.AuthorizationServer == "https://auth.example.com"),
                RequestingUserId,
                It.IsAny<CancellationToken>()),
                Times.Once);

            // The handshake against an address nothing listens on fails, so the overall outcome is false and
            // the failure (not a success) is what gets recorded.
            fixture.TokenStore.Verify(x => x.RecordConnectionResultAsync(ConnectionId, false, It.IsAny<string>(), RequestingUserId, It.IsAny<CancellationToken>()), Times.Once);
            Assert.False(completed);

            fixture.FlowsDbSet.Verify(x => x.Remove(flow), Times.Once);
            fixture.Db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
