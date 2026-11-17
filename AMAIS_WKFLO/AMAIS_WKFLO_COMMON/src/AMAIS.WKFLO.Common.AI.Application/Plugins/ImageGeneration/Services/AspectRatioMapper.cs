using System.Globalization;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    /// <summary>Maps a pixel size or ratio onto the discrete output ratios/sizes each image provider supports.</summary>
    internal static class AspectRatioMapper
    {
        private static readonly string[] GoogleRatios = { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" };

        private static readonly (string Size, double Ratio)[] AzureSizes =
        {
            ("1024x1024", 1.0),
            ("1536x1024", 1536.0 / 1024.0),
            ("1024x1536", 1024.0 / 1536.0)
        };

        /// <summary>Nearest Google-supported ratio (by log distance) for the given pixel size; null when invalid.</summary>
        public static string ToNearestGoogleAspectRatio(int width, int height) =>
            width > 0 && height > 0 ? NearestGoogle((double)width / height) : null;

        /// <summary>Nearest Azure gpt-image size for the given pixel size; null when invalid.</summary>
        public static string ToNearestAzureSize(int width, int height) =>
            width > 0 && height > 0 ? NearestAzure((double)width / height) : null;

        /// <summary>Nearest Azure gpt-image size for a "w:h" ratio string such as "16:9"; null when unparseable.</summary>
        public static string ToNearestAzureSize(string aspectRatio) =>
            TryParseRatio(aspectRatio, out var ratio) ? NearestAzure(ratio) : null;

        public static bool TryParseRatio(string aspectRatio, out double ratio)
        {
            ratio = 0;
            if (string.IsNullOrWhiteSpace(aspectRatio))
                return false;

            var parts = aspectRatio.Split(':');
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                || !double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0)
                return false;

            ratio = w / h;
            if (!double.IsFinite(ratio) || ratio <= 0)
            {
                ratio = 0;
                return false;
            }

            return true;
        }

        private static string NearestGoogle(double ratio)
        {
            string best = null;
            var bestDistance = double.MaxValue;
            foreach (var candidate in GoogleRatios)
            {
                TryParseRatio(candidate, out var candidateRatio);
                var distance = Math.Abs(Math.Log(ratio) - Math.Log(candidateRatio));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private static string NearestAzure(double ratio)
        {
            var best = AzureSizes[0].Size;
            var bestDistance = double.MaxValue;
            foreach (var (size, candidateRatio) in AzureSizes)
            {
                var distance = Math.Abs(Math.Log(ratio) - Math.Log(candidateRatio));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = size;
                }
            }

            return best;
        }
    }
}
