using System.Diagnostics.CodeAnalysis;

namespace AMAIS.WKFLO.DXP.MCP.Application.Pages
{
    /// <summary>
    /// Normalises CMS page paths and DXP document URLs so they can be compared, and builds the canonical page URL.
    /// </summary>
    /// <remarks>
    /// The CMS sends <c>pagePath = "/{alias lowercased}"</c>; DXP stores the page's <c>objectMetadata.canonicalURL</c>.
    /// Both sides are reduced to a lowercase, unescaped path with a single leading slash and no trailing slash,
    /// where "/" is the homepage.
    /// </remarks>
    public static class PagePath
    {
        public const string Homepage = "/";

        /// <summary>
        /// Normalises a page path received as a tool argument. Accepts "/alias", "alias", "/", or a full http(s) URL
        /// (only its path is used; the host always comes from the token). Query strings and fragments are dropped.
        /// </summary>
        public static bool TryNormalize(string? pagePath, [NotNullWhen(true)] out string? normalizedPath, [NotNullWhen(false)] out string? error)
        {
            normalizedPath = null;

            if (string.IsNullOrWhiteSpace(pagePath))
            {
                error = "pagePath is required. Use \"/\" for the homepage.";
                return false;
            }

            var value = pagePath.Trim();

            if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
                && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
            {
                value = absolute.AbsolutePath;
            }

            var cut = value.IndexOfAny(['?', '#']);
            if (cut >= 0)
            {
                value = value[..cut];
            }

            normalizedPath = NormalizePath(value);
            error = null;
            return true;
        }

        /// <summary>
        /// Path of a DXP document URL (<c>AggregationDocumentResponseResult.Object</c>), normalised the same way as
        /// <see cref="TryNormalize"/>. Returns null when the value is not an absolute http(s) URL.
        /// </summary>
        public static string? FromDocumentUrl(string? documentUrl)
        {
            if (Uri.TryCreate(documentUrl?.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                return NormalizePath(uri.AbsolutePath);
            }

            return null;
        }

        /// <summary>
        /// Canonical page URL: site origin (from the token's <c>aud</c>) plus the normalised path.
        /// The homepage is the origin followed by "/".
        /// </summary>
        public static Uri BuildCanonicalUrl(Uri siteOrigin, string normalizedPath)
        {
            var origin = siteOrigin.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
            return new Uri(origin + normalizedPath);
        }

        internal static string NormalizePath(string path)
        {
            var segments = Uri.UnescapeDataString(path)
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return segments.Length == 0
                ? Homepage
                : "/" + string.Join('/', segments).ToLowerInvariant();
        }
    }
}
