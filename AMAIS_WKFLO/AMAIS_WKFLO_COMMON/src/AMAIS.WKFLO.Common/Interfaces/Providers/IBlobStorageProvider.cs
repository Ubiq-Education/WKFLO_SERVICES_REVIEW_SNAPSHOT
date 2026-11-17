using AMAIS.WKFLO.Common.Models.Storage;

namespace AMAIS.WKFLO.Common.Interfaces.Providers
{
    public interface IBlobStorageProvider
    {
        /// <summary>
        /// Uploads a file to blob storage
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="blobName">Blob name</param>
        /// <param name="content">Content stream</param>
        /// <param name="contentType">Content type (MIME)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The blob URL</returns>
        Task<string> UploadAsync(string containerName, string blobName, Stream content, string contentType, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads a file to blob storage
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="blobName">Blob name</param>
        /// <param name="base64Content">Base64 encoded content</param>
        /// <param name="contentType">Content type (MIME)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The blob URL</returns>
        Task<string> UploadBase64Async(string containerName, string blobName, string base64Content, string contentType, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a file from blob storage
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="blobName">Blob name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>File content as stream</returns>
        Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a file from blob storage as base64 string
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="blobName">Blob name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Base64 encoded content</returns>
        Task<string> DownloadBase64Async(string containerName, string blobName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a blob exists
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="blobName">Blob name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if exists, false otherwise</returns>
        Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a blob
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="blobName">Blob name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if deleted, false otherwise</returns>
        Task<bool> DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists blobs from a container with a specified prefix and deserializes them to objects of type K
        /// </summary>
        /// <typeparam name="K">The type to deserialize blob content into (must be a class with parameterless constructor)</typeparam>
        /// <param name="containerName">Container name</param>
        /// <param name="prefix">Prefix to filter blobs (e.g. virtual directory path)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of deserialized objects from matching blobs</returns>
        Task<List<K>> ListAsync<K>(string containerName, string prefix, CancellationToken cancellationToken = default) where K : class, new();

        /// <summary>
        /// Gets the URL for a blob
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="blobName">Blob name</param>
        /// <returns>The blob URL</returns>
        string GetBlobUrl(string containerName, string blobName);

        /// <summary>
        /// Checks if multiple blobs exist in a container
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="blobNames">List of blob names to check</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with blob names as keys and boolean existence values</returns>
        Task<Dictionary<string, bool>> ExistsManyAsync(string containerName, IEnumerable<string> blobNames, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists blob names in a container with a specified prefix without deserializing content.
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="prefix">Prefix to filter blobs (use empty string for all blobs)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of blob names</returns>
        Task<List<string>> ListBlobNamesAsync(string containerName, string prefix, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the blobs in a container with a specified prefix, with their creation time, without downloading content.
        /// Returns an empty list when the container does not exist.
        /// </summary>
        /// <param name="containerName">Container name</param>
        /// <param name="prefix">Prefix to filter blobs (use empty string for all blobs)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The blobs' names and creation times</returns>
        Task<List<BlobListItem>> ListBlobsAsync(string containerName, string prefix, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a SAS URL for a blob, granting read access for the specified duration.
        /// The auth mode (account key vs managed identity) is controlled by BlobStorageSettings.SasAuthMode.
        /// </summary>
        /// <param name="blobUrl">The plain blob URL (e.g. https://account.blob.core.windows.net/container/path/file.png)</param>
        /// <param name="expiry">How long the SAS URL should remain valid</param>
        /// <returns>A SAS URL with a read token appended as query parameters</returns>
        Task<string> GenerateSasUrlAsync(Uri blobUrl, TimeSpan expiry);
    }
}
