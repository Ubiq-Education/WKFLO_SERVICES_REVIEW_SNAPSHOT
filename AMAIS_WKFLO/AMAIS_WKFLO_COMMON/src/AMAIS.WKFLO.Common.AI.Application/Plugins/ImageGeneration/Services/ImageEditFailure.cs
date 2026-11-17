namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    /// <summary>Provider-neutral category of an image edit failure (US 7564).</summary>
    public enum ImageEditFailureKind
    {
        SafetyBlocked,
        Overloaded,
        RateLimited,
        QuotaExceeded,
        InvalidRequest,
        NotConfigured,
        Other
    }

    /// <summary>
    /// An image edit failure a provider service has already classified (e.g. Google answered HTTP 200 without an
    /// image because a safety filter fired). Derives from <see cref="InvalidOperationException"/> so existing
    /// catch sites keep working; the message keeps the raw provider details for logs only.
    /// </summary>
    public sealed class ImageEditProviderException : InvalidOperationException
    {
        public ImageEditProviderException(ImageEditFailureKind kind, string provider, string message,
            string blockReason = null, IReadOnlyList<string> finishReasons = null,
            int? providerStatusCode = null, string providerStatus = null)
            : base(message)
        {
            Kind = kind;
            Provider = provider;
            BlockReason = blockReason;
            FinishReasons = finishReasons ?? Array.Empty<string>();
            ProviderStatusCode = providerStatusCode;
            ProviderStatus = providerStatus;
        }

        public ImageEditFailureKind Kind { get; }
        public string Provider { get; }
        public string BlockReason { get; }
        public IReadOnlyList<string> FinishReasons { get; }
        public int? ProviderStatusCode { get; }
        public string ProviderStatus { get; }
    }

    /// <summary>The user's request can't be served (no image in the conversation, bad file reference); the message is user-ready.</summary>
    public sealed class ImageEditInputException : InvalidOperationException
    {
        public ImageEditInputException(string message) : base(message) { }
        public ImageEditInputException(string message, Exception inner) : base(message, inner) { }
    }
}
