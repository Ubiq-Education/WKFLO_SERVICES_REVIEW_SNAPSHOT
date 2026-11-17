using AMAIS.WKFLO.Common.AI.Application.Services.Mcp;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Common.AI.Application.Services.Mcp
{
    public class McpClientServiceTests
    {
        private readonly McpClientService _service;

        public McpClientServiceTests()
        {
            _service = new McpClientService(
                Mock.Of<ILogger<McpClientService>>(),
                Mock.Of<IMcpOAuthTokenStore>(),
                BuildProtector().Object);
        }

        // Reversible fake: "enc:v1:" + Protect(x) stores "cipher(x)"; Unprotect reverses it. These tests are
        // about where the secret is decrypted, not the encryption itself (AesMcpTokenProtectorTests covers that).
        private static Mock<IMcpTokenProtector> BuildProtector()
        {
            var protector = new Mock<IMcpTokenProtector>();
            protector.Setup(x => x.Protect(It.IsAny<string>())).Returns((string s) => $"cipher({s})");
            protector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string s) => s.Substring("cipher(".Length, s.Length - "cipher(".Length - 1));
            return protector;
        }

        private static McpServerConfig BuildPreconfiguredClientConfig(string clientSecret) => new()
        {
            Id = "server-1",
            Name = "Canva",
            TransportType = TransportType.Http,
            Endpoint = "https://api.canva.com/connect/v1/mcp",
            AuthType = McpAuthType.OAuth2,
            OAuthScope = McpOAuthScope.PerUser,
            ClientId = "canva-client",
            ClientSecret = clientSecret,
            RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback"
        };

        [Fact]
        public void BuildOAuthOptions_EncryptedClientSecret_IsDecrypted()
        {
            var config = BuildPreconfiguredClientConfig(McpClientSecretHelper.EncryptedPrefix + "cipher(s3cret)");

            var options = _service.BuildOAuthOptions(config, Guid.NewGuid(), Guid.NewGuid());

            Assert.Equal("canva-client", options.ClientId);
            Assert.Equal("s3cret", options.ClientSecret);
        }

        [Fact]
        public void BuildOAuthOptions_LegacyPlaintextClientSecret_PassesThrough()
        {
            var options = _service.BuildOAuthOptions(BuildPreconfiguredClientConfig("legacy-plain"), Guid.NewGuid(), null);

            Assert.Equal("legacy-plain", options.ClientSecret);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void BuildOAuthOptions_NoClientSecret_IsNull(string clientSecret)
        {
            var options = _service.BuildOAuthOptions(BuildPreconfiguredClientConfig(clientSecret), Guid.NewGuid(), null);

            Assert.Null(options.ClientSecret);
        }

        [Fact]
        public void CreateTransport_Stdio_BuildsStdioClientTransport_Unchanged()
        {
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Test Server",
                TransportType = TransportType.Stdio,
                Command = "node",
                Arguments = new[] { "server.js" }
            };

            var transport = _service.CreateTransport(config);

            Assert.IsType<StdioClientTransport>(transport);
        }

        [Fact]
        public void CreateTransport_HttpWithoutOAuth_BuildsHttpClientTransport_WithStaticHeaders()
        {
            // Existing (non-OAuth) HTTP MCP servers — e.g. AMAIS's own MCP server via a fixed Bearer header —
            // must keep working exactly as before this feature.
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Test Server",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.example.com/mcp",
                AuthType = McpAuthType.None,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer static-token" }
            };

            var transport = _service.CreateTransport(config);

            Assert.IsType<HttpClientTransport>(transport);
        }

        [Fact]
        public void CreateTransport_HttpWithOAuth2_BuildsHttpClientTransport()
        {
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Humantic",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.humantic.ai/mcp",
                AuthType = McpAuthType.OAuth2,
                Scopes = new[] { "profiles:read" },
                RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback"
            };

            // mcpOAuthConnectionId is the owning RunableTool.McpOAuthConnectionId — required for OAuth2, and
            // deliberately unrelated to config.Id (McpClientService's own multi-server registry key, whatever
            // an admin typed).
            var transport = _service.CreateTransport(config, connectionId: Guid.Parse("11111111-1111-1111-1111-111111111111"));

            Assert.IsType<HttpClientTransport>(transport);
        }

        [Fact]
        public void CreateTransport_HttpWithOAuth2_NoConnectionId_ThrowsClearException()
        {
            // This combination should never happen once callers are correct (every OAuth2 caller must pass
            // the owning connection id), but if it does, it must fail loudly rather than silently keying
            // token storage/status tracking by the wrong thing.
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Humantic",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.humantic.ai/mcp",
                AuthType = McpAuthType.OAuth2,
                Scopes = new[] { "profiles:read" },
                RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback"
            };

            var exception = Assert.Throws<InvalidOperationException>(() => _service.CreateTransport(config, connectionId: null));
            Assert.Contains("Tool", exception.Message);
        }

        [Fact]
        public void CreateTransport_HttpWithOAuth2_PerUserScope_BuildsHttpClientTransport()
        {
            // Canva has no tenant/org-shared mode at all, so every tool connecting to it is OAuthScope =
            // PerUser — CreateTransport must accept a userId alongside connectionId for this case.
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Canva",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.canva.com/mcp",
                AuthType = McpAuthType.OAuth2,
                OAuthScope = McpOAuthScope.PerUser,
                Scopes = new[] { "design:content:read" },
                RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback"
            };

            var transport = _service.CreateTransport(
                config,
                connectionId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                userId: Guid.Parse("33333333-3333-3333-3333-333333333333"));

            Assert.IsType<HttpClientTransport>(transport);
        }

        [Fact]
        public void CreateTransport_UnsupportedTransportType_Throws()
        {
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Test Server",
                TransportType = (TransportType)99
            };

            Assert.Throws<ArgumentException>(() => _service.CreateTransport(config));
        }

        // --- Task 7130 addendum: McpOAuthInteractiveConsentRequiredException bypasses retry ---------------

        [Fact]
        public async Task CreateClientWithRetryAsync_InteractiveConsentRequired_DoesNotRetry()
        {
            // Retrying can never turn "needs interactive consent" into a success — this must get the same
            // early-exit treatment OperationCanceledException already gets, not the generic retry-with-backoff
            // loop every other exception goes through.
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Humantic",
                TransportType = TransportType.Http,
                Endpoint = "https://mcp.humantic.ai/mcp",
                AuthType = McpAuthType.OAuth2,
                Scopes = new[] { "profiles:read" },
                RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback",
                MaxRetries = 3
            };

            var attempts = 0;
            Task<McpClient> CreateClientAsync(IClientTransport transport, CancellationToken _)
            {
                attempts++;
                throw new McpOAuthInteractiveConsentRequiredException("needs interactive consent");
            }

            await Assert.ThrowsAsync<McpOAuthInteractiveConsentRequiredException>(() =>
                _service.CreateClientWithRetryAsync(config, connectionId: Guid.NewGuid(), userId: null, CancellationToken.None, CreateClientAsync));

            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task CreateClientWithRetryAsync_Http500OnDefaultProbe_ReconnectsOnFreshTransportViaConnector()
        {
            // Canva answers the SDK's default server/discover probe with HTTP 500; McpClientConnector must retry
            // with a pinned protocol version on a new transport instead of failing the connection.
            var config = BuildPreconfiguredClientConfig("legacy-plain");
            config.MaxRetries = 0;
            var transports = new List<IClientTransport>();
            var expected = new Mock<McpClient>().Object;

            Task<McpClient> CreateClientAsync(IClientTransport transport, CancellationToken _)
            {
                transports.Add(transport);
                if (transports.Count == 1)
                    throw new System.Net.Http.HttpRequestException("500", null, System.Net.HttpStatusCode.InternalServerError);
                return Task.FromResult(expected);
            }

            var client = await _service.CreateClientWithRetryAsync(config, connectionId: Guid.NewGuid(), userId: Guid.NewGuid(), CancellationToken.None, CreateClientAsync);

            Assert.Same(expected, client);
            Assert.Equal(2, transports.Count);
            Assert.NotSame(transports[0], transports[1]);
        }

        [Fact]
        public async Task CreateClientWithRetryAsync_ConnectionTimeoutElapses_ThrowsTimeoutNotCancellation()
        {
            var config = BuildPreconfiguredClientConfig("legacy-plain");
            config.MaxRetries = 0;
            config.ConnectionTimeout = TimeSpan.FromMilliseconds(50);
            CancellationToken observed = default;

            async Task<McpClient> HangsUntilCancelled(IClientTransport transport, CancellationToken ct)
            {
                observed = ct;
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            }

            var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
                _service.CreateClientWithRetryAsync(config, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None, HangsUntilCancelled));

            Assert.True(observed.CanBeCanceled);
            Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
        }

        [Fact]
        public async Task CreateClientWithRetryAsync_CallerCancels_StillReportedAsCancellation()
        {
            var config = BuildPreconfiguredClientConfig("legacy-plain");
            config.MaxRetries = 0;
            config.ConnectionTimeout = TimeSpan.FromMinutes(5);
            using var callerCts = new CancellationTokenSource();

            async Task<McpClient> CancelsCaller(IClientTransport transport, CancellationToken ct)
            {
                callerCts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _service.CreateClientWithRetryAsync(config, Guid.NewGuid(), Guid.NewGuid(), callerCts.Token, CancelsCaller));
        }
    }
}
