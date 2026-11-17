using Flurl.Http;
using Google.GenAI;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    /// <summary>Provider-specific details extracted from an image edit exception, for logging.</summary>
    public sealed record ImageEditFailureInfo(
        ImageEditFailureKind Kind, int? StatusCode, string Status, string Message, string BlockReason, string FinishReasons);

    /// <summary>Sorts image edit exceptions (Google SDK, Flurl/Azure, our own) into <see cref="ImageEditFailureKind"/>s (US 7564).</summary>
    public static class ImageEditErrorClassifier
    {
        // 429 is a rate limit unless there is a clear sign the usage cap itself is exhausted: the plain "exceeded your
        // current quota ... billing details" text (Gemini) and the quotaincrease link (Azure) also appear on per-minute limits.
        private static readonly string[] QuotaMarkers = { "insufficient_quota", "perday", "billing is disabled", "billing is not enabled", "billing not enabled", "billing_disabled" };
        private static readonly string[] SafetyMarkers = { "safety", "content_policy_violation", "moderation_blocked", "blocked", "prohibited" };

        public static async Task<ImageEditFailureKind> ClassifyAsync(Exception exception) => (await DescribeAsync(exception)).Kind;

        public static async Task<ImageEditFailureInfo> DescribeAsync(Exception exception)
        {
            switch (exception)
            {
                case ImageEditProviderException provider:
                    return new ImageEditFailureInfo(provider.Kind, provider.ProviderStatusCode, provider.ProviderStatus,
                        provider.Message, provider.BlockReason, string.Join(",", provider.FinishReasons));

                case ApiException api:
                    return Info(ClassifyHttp(api.StatusCode, api.Status, api.Message), api.StatusCode, api.Status, api.Message);

                case FlurlHttpTimeoutException:
                    return Info(ImageEditFailureKind.Overloaded, null, null, exception.Message);

                case FlurlHttpException flurl:
                {
                    var status = flurl.StatusCode;
                    var body = await SafeBodyAsync(flurl);
                    return Info(ClassifyHttp(status, null, $"{flurl.Message} {body}"), status, null, string.IsNullOrWhiteSpace(body) ? flurl.Message : body);
                }

                case TaskCanceledException or TimeoutException:
                    return Info(ImageEditFailureKind.Overloaded, null, null, exception.Message);

                default:
                    return Info(ImageEditFailureKind.Other, null, null, exception?.Message);
            }
        }

        private static ImageEditFailureInfo Info(ImageEditFailureKind kind, int? statusCode, string status, string message) =>
            new(kind, statusCode, status, message, null, null);

        private static async Task<string> SafeBodyAsync(FlurlHttpException ex)
        {
            try
            {
                return await ex.GetResponseStringAsync();
            }
            catch (Exception)
            {
                // The body is only used to refine the classification and the log text; the status code still classifies.
                return null;
            }
        }

        private static ImageEditFailureKind ClassifyHttp(int? statusCode, string status, string text)
        {
            var normalizedStatus = status?.ToUpperInvariant();
            var lower = text?.ToLowerInvariant() ?? string.Empty;

            if (statusCode == 429 || normalizedStatus == "RESOURCE_EXHAUSTED")
                return QuotaMarkers.Any(lower.Contains) ? ImageEditFailureKind.QuotaExceeded : ImageEditFailureKind.RateLimited;

            if (statusCode == 408 || statusCode >= 500 || normalizedStatus is "UNAVAILABLE" or "INTERNAL" or "DEADLINE_EXCEEDED"
                || lower.Contains("overloaded"))
                return ImageEditFailureKind.Overloaded;

            if (statusCode is 401 or 403 or 404 || normalizedStatus is "PERMISSION_DENIED" or "NOT_FOUND" or "UNAUTHENTICATED")
                return ImageEditFailureKind.NotConfigured;

            if (lower.Contains("api key") || lower.Contains("api_key_invalid"))
                return ImageEditFailureKind.NotConfigured;

            if (statusCode == 400 || normalizedStatus is "INVALID_ARGUMENT" or "FAILED_PRECONDITION")
                return SafetyMarkers.Any(lower.Contains) ? ImageEditFailureKind.SafetyBlocked : ImageEditFailureKind.InvalidRequest;

            return ImageEditFailureKind.Other;
        }
    }
}
