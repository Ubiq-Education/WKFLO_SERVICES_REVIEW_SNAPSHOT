using AMAIS.WKFLO.Common.Models.Config;
using Newtonsoft.Json;
using System;

namespace AMAIS.WKFLO.AI.UnitTests.Common.Models.Config
{
    public class McpServerConfigTests
    {
        [Fact]
        public void Serialize_TransportTypeAndAuthType_WriteAsStrings_NotInts()
        {
            // Regression test: StartToolMcpOAuthCommandHandler's RedirectUri-backfill rewrite round-trips
            // this config through JsonConvert.SerializeObject. Without a StringEnumConverter, Newtonsoft's
            // default enum handling writes the underlying int (transportType:1, authType:1) instead of the
            // strings wkflo_ui always writes/expects (transportType:"Http", authType:"OAuth2") — confirmed
            // directly against a corrupted DB row from local testing.
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Test Server",
                TransportType = TransportType.Http,
                AuthType = McpAuthType.OAuth2
            };

            var json = JsonConvert.SerializeObject(config);

            Assert.Contains("\"transportType\":\"Http\"", json);
            Assert.Contains("\"authType\":\"OAuth2\"", json);
        }
        private static McpServerConfig BuildBaseConfig() => new McpServerConfig
        {
            Id = "server-1",
            Name = "Test Server"
        };

        private static McpServerConfig BuildValidOAuth2Config()
        {
            var config = BuildBaseConfig();
            config.AuthType = McpAuthType.OAuth2;
            config.TransportType = TransportType.Http;
            config.Endpoint = "https://mcp.humantic.ai/mcp";
            config.RedirectUri = "https://wkflo.example.com/md/api/v1/tenant/tool/mcp/oauth/callback";
            return config;
        }

        [Fact]
        public void Validate_OAuth2_DefaultsAuthTypeToNone()
        {
            var config = new McpServerConfig();

            Assert.Equal(McpAuthType.None, config.AuthType);
        }

        [Fact]
        public void Validate_OAuth2_WithValidEndpointAndRedirectUri_DoesNotThrow()
        {
            var config = BuildValidOAuth2Config();

            var exception = Record.Exception(() => config.Validate());

            Assert.Null(exception);
        }

        [Fact]
        public void Validate_OAuth2_DoesNotRequireCommand()
        {
            // OAuth2 is only ever used over HTTP; unlike TransportType.Stdio it must never require Command,
            // regardless of what TransportType happens to be set to (default is Stdio).
            var config = BuildValidOAuth2Config();
            config.Command = string.Empty;

            var exception = Record.Exception(() => config.Validate());

            Assert.Null(exception);
        }

        [Fact]
        public void Validate_OAuth2_NonHttpTransport_Throws()
        {
            // Without this check, an OAuth2 config left at the default TransportType (Stdio) would pass
            // Validate() cleanly, then McpClientService.CreateTransport switches on TransportType alone and
            // would silently build a Stdio transport at connect time, ignoring OAuth entirely.
            var config = BuildValidOAuth2Config();
            config.TransportType = TransportType.Stdio;

            var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Http transport", exception.Message);
        }

        [Fact]
        public void Validate_OAuth2_MissingEndpoint_Throws()
        {
            var config = BuildValidOAuth2Config();
            config.Endpoint = string.Empty;

            var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Endpoint", exception.Message);
        }

        [Fact]
        public void Validate_OAuth2_InvalidEndpointUri_Throws()
        {
            var config = BuildValidOAuth2Config();
            config.Endpoint = "not-a-uri";

            var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("valid URI", exception.Message);
        }

        [Fact]
        public void Validate_OAuth2_MissingRedirectUri_Throws()
        {
            // The ModelContextProtocol SDK's ClientOAuthProvider requires RedirectUri to construct the
            // transport at all, even for a silent/cached-token reconnect with no interactive callback — see
            // McpServerConfig.Validate()'s remarks.
            var config = BuildValidOAuth2Config();
            config.RedirectUri = string.Empty;

            var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("RedirectUri", exception.Message);
        }

        [Fact]
        public void Validate_OAuth2_InvalidRedirectUri_Throws()
        {
            var config = BuildValidOAuth2Config();
            config.RedirectUri = "not-a-uri";

            var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("RedirectUri", exception.Message);
        }

        [Fact]
        public void Validate_None_Http_StillRequiresEndpoint()
        {
            // Non-OAuth behavior must stay exactly as before: AuthType = None + Http still requires Endpoint.
            var config = BuildBaseConfig();
            config.AuthType = McpAuthType.None;
            config.TransportType = TransportType.Http;
            config.Endpoint = string.Empty;

            var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Endpoint", exception.Message);
        }

        [Fact]
        public void Validate_None_Http_DoesNotRequireRedirectUri()
        {
            // Non-OAuth HTTP configs (e.g. a fixed Bearer token via AdditionalHeaders) never need RedirectUri.
            var config = BuildBaseConfig();
            config.AuthType = McpAuthType.None;
            config.TransportType = TransportType.Http;
            config.Endpoint = "https://mcp.example.com/mcp";

            var exception = Record.Exception(() => config.Validate());

            Assert.Null(exception);
        }

        [Fact]
        public void Validate_None_Stdio_StillRequiresCommand()
        {
            // Non-OAuth behavior must stay exactly as before: AuthType = None + Stdio still requires Command.
            var config = BuildBaseConfig();
            config.AuthType = McpAuthType.None;
            config.TransportType = TransportType.Stdio;
            config.Command = string.Empty;

            var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("Command", exception.Message);
        }

        [Fact]
        public void Validate_None_Stdio_WithCommand_DoesNotThrow()
        {
            var config = BuildBaseConfig();
            config.AuthType = McpAuthType.None;
            config.TransportType = TransportType.Stdio;
            config.Command = "node";

            var exception = Record.Exception(() => config.Validate());

            Assert.Null(exception);
        }

        // --- OAuthScope (Shared vs PerUser, e.g. Canva) --------------------------------------------------

        [Fact]
        public void OAuthScope_DefaultsToShared()
        {
            var config = new McpServerConfig();

            Assert.Equal(McpOAuthScope.Shared, config.OAuthScope);
        }

        [Fact]
        public void Validate_OAuth2_PerUserScope_WithValidConfig_DoesNotThrow()
        {
            var config = BuildValidOAuth2Config();
            config.OAuthScope = McpOAuthScope.PerUser;

            var exception = Record.Exception(() => config.Validate());

            Assert.Null(exception);
        }

        [Fact]
        public void Validate_PerUserScope_WithoutOAuth2_Throws()
        {
            // Per-user token storage only makes sense for an OAuth2 server; nothing else ever persists a
            // ToolOAuthToken row to begin with.
            var config = BuildBaseConfig();
            config.AuthType = McpAuthType.None;
            config.TransportType = TransportType.Http;
            config.Endpoint = "https://mcp.example.com/mcp";
            config.OAuthScope = McpOAuthScope.PerUser;

            var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.Contains("OAuthScope", exception.Message);
        }

        [Fact]
        public void Serialize_OAuthScope_WritesAsString_NotInt()
        {
            var config = new McpServerConfig
            {
                Id = "server-1",
                Name = "Canva",
                OAuthScope = McpOAuthScope.PerUser
            };

            var json = JsonConvert.SerializeObject(config);

            Assert.Contains("\"oAuthScope\":\"PerUser\"", json);
        }
    }
}
