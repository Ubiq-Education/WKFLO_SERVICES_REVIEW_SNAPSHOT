using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Config;
using Newtonsoft.Json;
using System;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp
{
    public class McpOAuthScopeResolverTests
    {
        private static string SharedConfig() => JsonConvert.SerializeObject(new McpServerConfig
        {
            Id = "humantic",
            Name = "Humantic",
            TransportType = TransportType.Http,
            Endpoint = "https://mcp.humantic.ai/mcp",
            AuthType = McpAuthType.OAuth2,
            OAuthScope = McpOAuthScope.Shared
        });

        private static string PerUserConfig() => JsonConvert.SerializeObject(new McpServerConfig
        {
            Id = "canva",
            Name = "Canva",
            TransportType = TransportType.Http,
            Endpoint = "https://mcp.canva.com/mcp",
            AuthType = McpAuthType.OAuth2,
            OAuthScope = McpOAuthScope.PerUser
        });

        [Fact]
        public void ResolveRequestingUserId_SharedScope_AlwaysReturnsNull()
        {
            var result = McpOAuthScopeResolver.ResolveRequestingUserId(SharedConfig(), Guid.NewGuid().ToString());

            Assert.Null(result);
        }

        [Fact]
        public void ResolveRequestingUserId_SharedScope_IgnoresEvenAnUnparsableUserContextGuid()
        {
            // A Shared tool never needs a caller identity at all - an unresolvable one must not become an
            // error for it, since it's simply irrelevant to this mode.
            var result = McpOAuthScopeResolver.ResolveRequestingUserId(SharedConfig(), "not-a-guid");

            Assert.Null(result);
        }

        [Fact]
        public void ResolveRequestingUserId_PerUserScope_ReturnsTheParsedUserId()
        {
            var userId = Guid.NewGuid();

            var result = McpOAuthScopeResolver.ResolveRequestingUserId(PerUserConfig(), userId.ToString());

            Assert.Equal(userId, result);
        }

        // Regression test (Copilot PR review, WI 7152): this must throw, not return null. Null is also the
        // exact key a Shared-scope row is stored under (see ToolOAuthTokenConfiguration's filtered indexes),
        // so silently resolving "no identity available" to null here would let a PerUser tool's caller
        // fall through to reading/deleting whatever Shared row happens to exist for the same connection
        // (e.g. left over from before the tool's OAuthScope was switched) - a real, different token
        // belonging to a different mode entirely. Every caller must get an unmistakable signal instead.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-guid")]
        public void ResolveRequestingUserId_PerUserScope_UnresolvableUserContextGuid_ThrowsRatherThanReturningNull(string userContextGuid)
        {
            Assert.Throws<McpOAuthPerUserIdentityUnavailableException>(
                () => McpOAuthScopeResolver.ResolveRequestingUserId(PerUserConfig(), userContextGuid));
        }

        [Fact]
        public void ResolveRequestingUserId_MalformedToolConfig_TreatsAsSharedScope()
        {
            // Matches IsPerUserScope's existing tolerant-parsing behavior: malformed/legacy config JSON that
            // can't be deserialized is treated as Shared (the default), not as an error.
            var result = McpOAuthScopeResolver.ResolveRequestingUserId("not json", Guid.NewGuid().ToString());

            Assert.Null(result);
        }
    }
}
