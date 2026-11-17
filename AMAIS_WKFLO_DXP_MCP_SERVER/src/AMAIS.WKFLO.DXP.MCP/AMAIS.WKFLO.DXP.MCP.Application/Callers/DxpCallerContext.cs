using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace AMAIS.WKFLO.DXP.MCP.Application.Callers
{
    /// <summary>
    /// Who is calling the DXP MCP, resolved only from the validated CMS-issued JWT.
    /// </summary>
    /// <remarks>
    /// The DXP Report API trusts the <c>amais-dxp-tenant-name</c> header it receives, so the tenant must never come
    /// from tool arguments (which the model, and therefore the end user, controls). It is always the token's
    /// <c>site_id</c> claim, whose value is the CMS <c>Application</c> name, i.e. the DXP tenant name.
    /// </remarks>
    public sealed record DxpCallerContext(string TenantName, Uri? SiteOrigin)
    {
        public const string TenantClaimType = "site_id";
        public const string AudienceClaimType = "aud";

        public const string MissingTenantError = "The access token has no site_id claim, so the DXP site could not be identified.";
        public const string MissingSiteOriginError = "The access token has no valid site URL (aud claim), so the page URL could not be built.";

        /// <summary>
        /// Resolves the caller from the authenticated principal. Fails when the principal is not authenticated or
        /// has no non-empty <c>site_id</c> claim. <see cref="SiteOrigin"/> is optional here; tools that need it check it.
        /// </summary>
        public static bool TryResolve(ClaimsPrincipal? principal, [NotNullWhen(true)] out DxpCallerContext? caller, [NotNullWhen(false)] out string? error)
        {
            caller = null;

            if (principal?.Identity?.IsAuthenticated != true)
            {
                error = "User is not authenticated.";
                return false;
            }

            var tenantName = principal.FindFirst(TenantClaimType)?.Value?.Trim();
            if (string.IsNullOrEmpty(tenantName))
            {
                error = MissingTenantError;
                return false;
            }

            caller = new DxpCallerContext(tenantName, ResolveSiteOrigin(principal));
            error = null;
            return true;
        }

        /// <summary>
        /// The CMS sets <c>aud</c> to the site origin (<c>{scheme}://{host}</c>). Returns the first <c>aud</c> value that is
        /// an absolute http(s) URL, reduced to its origin, or null when there is none.
        /// </summary>
        internal static Uri? ResolveSiteOrigin(ClaimsPrincipal principal)
        {
            foreach (var claim in principal.FindAll(AudienceClaimType))
            {
                if (Uri.TryCreate(claim.Value?.Trim(), UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                {
                    return new Uri(uri.GetLeftPart(UriPartial.Authority));
                }
            }

            return null;
        }
    }
}
