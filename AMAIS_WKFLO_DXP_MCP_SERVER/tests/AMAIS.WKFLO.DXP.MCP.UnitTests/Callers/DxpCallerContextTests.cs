using AMAIS.WKFLO.DXP.MCP.Application.Callers;

namespace AMAIS.WKFLO.DXP.MCP.UnitTests.Callers
{
    public class DxpCallerContextTests
    {
        [Fact]
        public void TryResolve_WithSiteIdClaim_UsesClaimAsTenant()
        {
            var resolved = DxpCallerContext.TryResolve(TestPrincipals.Authenticated("  my-site  "), out var caller, out var error);

            Assert.True(resolved);
            Assert.Null(error);
            Assert.Equal("my-site", caller!.TenantName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryResolve_WithoutSiteIdClaim_Fails(string? siteId)
        {
            var resolved = DxpCallerContext.TryResolve(TestPrincipals.Authenticated(siteId), out var caller, out var error);

            Assert.False(resolved);
            Assert.Null(caller);
            Assert.Equal(DxpCallerContext.MissingTenantError, error);
        }

        [Fact]
        public void TryResolve_WhenNotAuthenticated_Fails()
        {
            var resolved = DxpCallerContext.TryResolve(TestPrincipals.Anonymous(), out var caller, out var error);

            Assert.False(resolved);
            Assert.Null(caller);
            Assert.NotNull(error);
        }

        [Fact]
        public void TryResolve_WhenPrincipalNull_Fails()
        {
            Assert.False(DxpCallerContext.TryResolve(null, out _, out _));
        }

        [Theory]
        [InlineData("https://www.example.org", "https://www.example.org/")]
        [InlineData("https://www.example.org/some/path?x=1", "https://www.example.org/")]
        [InlineData("http://localhost:44332", "http://localhost:44332/")]
        public void TryResolve_SiteOriginIsAudOrigin(string audience, string expectedOrigin)
        {
            DxpCallerContext.TryResolve(TestPrincipals.Authenticated(audiences: audience), out var caller, out _);

            Assert.Equal(expectedOrigin, caller!.SiteOrigin!.AbsoluteUri);
        }

        [Fact]
        public void TryResolve_SkipsNonUrlAudiences()
        {
            DxpCallerContext.TryResolve(TestPrincipals.Authenticated(audiences: ["api://some-app", "https://site.example.org"]), out var caller, out _);

            Assert.Equal("https://site.example.org/", caller!.SiteOrigin!.AbsoluteUri);
        }

        [Fact]
        public void TryResolve_WithoutUrlAudience_HasNoSiteOrigin()
        {
            var resolved = DxpCallerContext.TryResolve(TestPrincipals.Authenticated(audiences: "not-a-url"), out var caller, out _);

            Assert.True(resolved);
            Assert.Null(caller!.SiteOrigin);
        }
    }
}
