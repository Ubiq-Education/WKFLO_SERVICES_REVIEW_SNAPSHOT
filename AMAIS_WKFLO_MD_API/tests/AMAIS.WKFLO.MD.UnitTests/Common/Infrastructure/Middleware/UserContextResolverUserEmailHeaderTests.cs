using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using AMAIS.WKFLO.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.Infrastructure.Middleware
{
    /// <summary>
    /// Bug 7527: PetraCMS calls WKFlo with a Managed Identity outside local dev - an app-only Entra ID token
    /// with no user claims - so it passes the acting admin's email in the amais-wkflo-user-email header. The
    /// header must only be honoured for Entra ID app-only tokens: a real user token always keeps its own
    /// identity, and an AMAIS-issued JWT (held by end users' browsers) must never be able to choose one.
    /// </summary>
    public class UserContextResolverUserEmailHeaderTests
    {
        private const string HeaderEmail = "admin@school.edu";
        private const string EntraV2Issuer = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0";
        private const string EntraV1Issuer = "https://sts.windows.net/11111111-1111-1111-1111-111111111111/";

        private static async Task<UserContext> ResolveAsync(IEnumerable<Claim> claims, string userEmailHeader)
        {
            var userContext = new UserContext();
            var services = new ServiceCollection();
            services.AddSingleton(userContext);

            var httpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider(),
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            };
            if (userEmailHeader != null)
            {
                httpContext.Request.Headers[CommonConsts.UserEmail] = userEmailHeader;
            }

            await new UserContextResolver(_ => Task.CompletedTask).Invoke(httpContext);
            return userContext;
        }

        [Theory]
        [InlineData(EntraV2Issuer)]
        [InlineData(EntraV1Issuer)]
        public async Task EntraAppOnlyToken_WithUserEmailHeader_UsesHeaderEmail(string issuer)
        {
            var claims = new[] { new Claim("iss", issuer), new Claim("oid", "managed-identity-oid"), new Claim("idtyp", "app") };

            var userContext = await ResolveAsync(claims, HeaderEmail);

            Assert.Equal(HeaderEmail, userContext.Email);
            Assert.Equal(HeaderEmail, userContext.UserName);
        }

        [Fact]
        public async Task EntraAppOnlyTokenWithoutIdtypClaim_WithUserEmailHeader_UsesHeaderEmail()
        {
            // No idtyp (optional claim not emitted) and no delegated scp claim - still app-only.
            var claims = new[] { new Claim("iss", EntraV2Issuer), new Claim("oid", "managed-identity-oid") };

            var userContext = await ResolveAsync(claims, HeaderEmail);

            Assert.Equal(HeaderEmail, userContext.Email);
        }

        [Fact]
        public async Task AmaisIssuedJwtWithoutEmail_WithUserEmailHeader_IgnoresHeader()
        {
            // AMAIS JWTs (AI API AmaisJwtScheme / MCP Server) never carry scp, so without the issuer check they
            // would look app-only and let a browser user impersonate anyone by email.
            var claims = new[] { new Claim("iss", "https://amais.example/issuer"), new Claim("sub", "widget-visitor") };

            var userContext = await ResolveAsync(claims, HeaderEmail);

            Assert.Equal(string.Empty, userContext.Email);
        }

        [Fact]
        public async Task UserToken_WithUserEmailHeader_KeepsTokenEmail()
        {
            var claims = new[] { new Claim("iss", EntraV2Issuer), new Claim("scp", "access_as_user"), new Claim("email", "real.user@school.edu") };

            var userContext = await ResolveAsync(claims, HeaderEmail);

            Assert.Equal("real.user@school.edu", userContext.Email);
        }

        [Theory]
        [InlineData("scp")]
        [InlineData("http://schemas.microsoft.com/identity/claims/scope")]
        public async Task DelegatedTokenWithoutEmail_WithUserEmailHeader_IgnoresHeader(string scopeClaimType)
        {
            // Covers both the raw "scp" claim and the type JwtBearer maps it to with inbound claim mapping.
            var claims = new[] { new Claim("iss", EntraV2Issuer), new Claim(scopeClaimType, "access_as_user") };

            var userContext = await ResolveAsync(claims, HeaderEmail);

            Assert.Equal(string.Empty, userContext.Email);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-an-email")]
        [InlineData("@")]
        [InlineData("admin@school.edu extra")]
        [InlineData("Admin <admin@school.edu>")]
        public async Task EntraAppOnlyToken_WithMissingOrInvalidHeader_LeavesEmailEmpty(string userEmailHeader)
        {
            var claims = new[] { new Claim("iss", EntraV2Issuer), new Claim("idtyp", "app") };

            var userContext = await ResolveAsync(claims, userEmailHeader);

            Assert.Equal(string.Empty, userContext.Email);
        }
    }
}
