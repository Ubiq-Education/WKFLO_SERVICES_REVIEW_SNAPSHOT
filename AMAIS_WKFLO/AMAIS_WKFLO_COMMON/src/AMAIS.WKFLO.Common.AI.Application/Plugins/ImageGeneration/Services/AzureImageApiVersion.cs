using System.Text.RegularExpressions;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    /// <summary>Azure OpenAI api-version rules for the images/edits endpoint.</summary>
    internal static class AzureImageApiVersion
    {
        public const string Default = "2025-04-01-preview";

        private const string FirstVersionWithEditSize = "2025-04-01";

        private static readonly Regex PreviewVersion = new(@"^(\d{4}-\d{2}-\d{2})-preview$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// True only for a dated "YYYY-MM-DD-preview" version at or after 2025-04-01-preview, the first version documented
        /// to accept `size` on images/edits (gpt-image-1). Null, blank, GA ("2025-04-01") and unrecognized values are false.
        /// </summary>
        public static bool SupportsEditSize(string apiVersion)
        {
            if (string.IsNullOrWhiteSpace(apiVersion))
                return false;

            var match = PreviewVersion.Match(apiVersion.Trim());
            return match.Success && string.CompareOrdinal(match.Groups[1].Value, FirstVersionWithEditSize) >= 0;
        }
    }
}
