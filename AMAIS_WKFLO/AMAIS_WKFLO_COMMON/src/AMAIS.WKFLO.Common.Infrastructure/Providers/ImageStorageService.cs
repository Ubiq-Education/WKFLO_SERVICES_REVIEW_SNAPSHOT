using AMAIS.WKFLO.Common.Interfaces;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    /// <summary>
    /// Implementation of <see cref="IImageStorageService"/>. Thin wrapper around the existing
    /// <see cref="IBlobStorageProvider.UploadAsync"/> — does not reimplement blob upload, and does
    /// not invent a naming policy of its own; the caller fully controls container/path/filename.
    /// </summary>
    public class ImageStorageService : IImageStorageService
    {
        // No appsettings-bound config, no hardcoded per-environment URLs: the base URL for the
        // public static website endpoint ("$web" container root) is read directly from this env
        // var, same as other cross-cutting WKFLO settings (e.g. DEFAULT_WEB_ORIGINS,
        // DB_CONNECTION_STRING). Staging and Production each set their own value in their Azure
        // App Service configuration; local dev sets it in appsettings.Development.json (gitignored).
        private const string EnvironmentVariableName = "STATIC_WEBSITE_BASE_URL";

        private readonly IBlobStorageProvider _blobStorageProvider;

        public ImageStorageService(IBlobStorageProvider blobStorageProvider)
        {
            _blobStorageProvider = blobStorageProvider;
        }

        /// <inheritdoc/>
        public async Task<ImageStorageResult> SaveImageAsync(string containerName, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            await _blobStorageProvider.UploadAsync(containerName, relativePath, content, contentType, cancellationToken);

            var trimmedRelativePath = relativePath?.TrimStart('/');
            var baseUrl = Environment.GetEnvironmentVariable(EnvironmentVariableName)?.TrimEnd('/');

            return new ImageStorageResult
            {
                RelativePath = relativePath,
                // If the env var isn't configured, surface a null AbsoluteUrl rather than throwing —
                // the upload itself still succeeded and RelativePath is still usable/persistable;
                // this mirrors how the earlier BlobStorageSettings-based version behaved when unset.
                AbsoluteUrl = string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl}/{trimmedRelativePath}"
            };
        }
    }
}
