using System.Security.Claims;

namespace AMAIS.WKFLO.DXP.MCP.UnitTests
{
    internal static class TestPrincipals
    {
        public const string Tenant = "tenant-from-token";
        public const string Origin = "https://www.example.org";

        public static ClaimsPrincipal Authenticated(string? siteId = Tenant, params string[] audiences)
        {
            var claims = new List<Claim> { new("sub", "user-1") };
            if (siteId != null)
            {
                claims.Add(new Claim("site_id", siteId));
            }

            foreach (var audience in audiences.Length == 0 ? [Origin] : audiences)
            {
                claims.Add(new Claim("aud", audience));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }

        public static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());
    }
}
