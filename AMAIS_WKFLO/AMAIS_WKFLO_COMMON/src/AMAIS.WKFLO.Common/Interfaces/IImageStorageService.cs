using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.Common.Interfaces
{
    /// <summary>
    /// Generic image storage service. Wraps <see cref="Providers.IBlobStorageProvider.UploadAsync"/> —
    /// the caller decides the container, relative path and filename; this service does not invent any
    /// naming/anonymization policy of its own. Reusable by any feature that needs to persist an
    /// image/icon/resource to blob storage (not specific to chat icons).
    /// </summary>
    public interface IImageStorageService
    {
        /// <summary>
        /// Uploads image content to blob storage at the caller-supplied path.
        /// </summary>
        /// <param name="containerName">Container name (e.g. the public "$web" static website container)</param>
        /// <param name="relativePath">Caller-supplied relative path/filename within the container</param>
        /// <param name="content">Image content stream</param>
        /// <param name="contentType">Content type (MIME)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The relative path (as stored) and the absolute URL the image is reachable at</returns>
        Task<ImageStorageResult> SaveImageAsync(string containerName, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default);
    }
}
