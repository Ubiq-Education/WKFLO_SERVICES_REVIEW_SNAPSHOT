using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.Common.Models.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    /// <summary>
    /// Implementation of IBlobStorageProvider for Azure Blob Storage operations
    /// </summary>
    public class BlobStorageProvider : IBlobStorageProvider
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _storageBaseUrl;
        private readonly string _baseCdnUrl;
        private readonly string _sasAuthMode;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<BlobStorageProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the BlobStorageProvider
        /// </summary>
        /// <param name="options">Blob storage configuration options</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <exception cref="ArgumentException">Thrown when connection string is not configured</exception>
        public BlobStorageProvider(IOptions<BlobStorageSettings> options, IDateTimeProvider dateTimeProvider, ILogger<BlobStorageProvider> logger)
        {
            var settings = options.Value;
            _blobServiceClient = CreateBlobServiceClient(settings);

            // Use provided base URL or fallback to the client's URI
            _storageBaseUrl = settings.BaseUrl ?? _blobServiceClient.Uri.AbsoluteUri;
            _baseCdnUrl = settings.BaseCdnUrl;
            _sasAuthMode = settings.SasAuthMode ?? "AccountKey";
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<string> UploadAsync(string containerName, string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get container client and create it if it doesn't exist
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

                // Get blob client for the specified blob name
                var blobClient = containerClient.GetBlobClient(blobName);

                // Set content type in blob headers
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                };

                // Upload content with specified headers
                await blobClient.UploadAsync(content, new BlobUploadOptions { HttpHeaders = blobHttpHeaders }, cancellationToken);

                // Return the absolute URI to the uploaded blob
                return blobClient.Uri.AbsoluteUri;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading blob {BlobRef} to container {ContainerName}", BlobNameForLog(blobName), containerName);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<string> UploadBase64Async(string containerName, string blobName, string base64Content, string contentType, CancellationToken cancellationToken = default)
        {
            // Validate base64 content
            if (string.IsNullOrEmpty(base64Content))
            {
                throw new ArgumentException("Base64 content cannot be null or empty", nameof(base64Content));
            }

            // Convert base64 string to byte array
            byte[] bytes = Convert.FromBase64String(base64Content);

            // Create memory stream from bytes and upload
            using var stream = new MemoryStream(bytes);
            return await UploadAsync(containerName, blobName, stream, contentType, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get container and blob clients
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                // Check if blob exists before attempting download
                if (!await blobClient.ExistsAsync(cancellationToken))
                {
                    _logger.LogWarning("Blob {BlobRef} not found in container {ContainerName}", BlobNameForLog(blobName), containerName);
                    return null;
                }

                // Download blob content to memory stream
                var memoryStream = new MemoryStream();
                await blobClient.DownloadToAsync(memoryStream, cancellationToken);

                // Reset position to beginning of stream for reading
                memoryStream.Position = 0;

                return memoryStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading blob {BlobRef} from container {ContainerName}", BlobNameForLog(blobName), containerName);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<string> DownloadBase64Async(string containerName, string blobName, CancellationToken cancellationToken = default)
        {
            // Download blob as stream
            using var stream = await DownloadAsync(containerName, blobName, cancellationToken);

            // Return null if blob doesn't exist
            if (stream == null)
            {
                return null;
            }

            // Convert stream content to base64 string
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            return Convert.ToBase64String(memoryStream.ToArray());
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get container client
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // Check if container exists first
                if (!await containerClient.ExistsAsync(cancellationToken))
                {
                    return false;
                }

                // Check if blob exists within the container
                var blobClient = containerClient.GetBlobClient(blobName);
                return await blobClient.ExistsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence of blob {BlobRef} in container {ContainerName}", BlobNameForLog(blobName), containerName);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get container client
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // Return false if container doesn't exist
                if (!await containerClient.ExistsAsync(cancellationToken))
                {
                    return false;
                }

                // Get blob client
                var blobClient = containerClient.GetBlobClient(blobName);

                // Return false if blob doesn't exist
                if (!await blobClient.ExistsAsync(cancellationToken))
                {
                    return false;
                }

                // Delete the blob
                var response = await blobClient.DeleteAsync(cancellationToken: cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blob {BlobRef} from container {ContainerName}", BlobNameForLog(blobName), containerName);
                throw;
            }
        }

        /// <summary>
        /// Lists blobs in a container with a specified prefix and deserializes them to objects of type K
        /// </summary>
        /// <typeparam name="K">The type to deserialize the blob content into</typeparam>
        /// <param name="containerName">Container name</param>
        /// <param name="prefix">Prefix to filter blobs (e.g. virtual directory path)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A list of deserialized objects from matching blobs</returns>
        /// <exception cref="Exception">Thrown when an error occurs during listing or deserialization</exception>
        public async Task<List<K>> ListAsync<K>(string containerName, string prefix, CancellationToken cancellationToken = default)
            where K : class, new()
        {
            try
            {
                // Get container client
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // Check if the container exists
                if (!await containerClient.ExistsAsync())
                {
                    return new List<K>();
                }

                // List blobs with the specific prefix
                var blobs = containerClient.GetBlobsAsync(
                    Azure.Storage.Blobs.Models.BlobTraits.None,
                    Azure.Storage.Blobs.Models.BlobStates.None,
                    prefix,
                    cancellationToken);

                var results = new List<K>();

                await foreach (var blob in blobs)
                {
                    // Filter for JSON files only
                    var blobName = blob.Name;
                    if (!blobName.EndsWith(".json")) continue;

                    var parts = blobName.Split('/');
                    if (parts.Length < 4) continue;

                    var sessionId = Path.GetFileNameWithoutExtension(parts[parts.Length - 1]);

                    // Download the blob to get the details
                    var download = await DownloadAsync(containerName, blobName);

                    if (download != null)
                    {
                        using var reader = new StreamReader(download);
                        var json = await reader.ReadToEndAsync();
                        var persisted = System.Text.Json.JsonSerializer.Deserialize<K>(json);

                        if (persisted != null)
                        {
                            results.Add(persisted);
                        }
                    }
                }

                return results.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing blob prefix {prefix} from container {ContainerName}", prefix, containerName);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<List<string>> ListBlobNamesAsync(string containerName, string prefix, CancellationToken cancellationToken = default)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                if (!await containerClient.ExistsAsync(cancellationToken))
                    return [];

                var names = new List<string>();
                await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
                    names.Add(blob.Name);

                return names;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing blob names with prefix {Prefix} in container {ContainerName}", prefix, containerName);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<List<BlobListItem>> ListBlobsAsync(string containerName, string prefix, CancellationToken cancellationToken = default)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                if (!await containerClient.ExistsAsync(cancellationToken))
                    return [];

                var blobs = new List<BlobListItem>();
                await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
                    blobs.Add(ToBlobListItem(blob));

                return blobs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing blobs with prefix {Prefix} in container {ContainerName}", prefix, containerName);
                throw;
            }
        }

        /// <summary>A log-safe reference to a blob (no file name); see <see cref="BlobNameLogFormatter.ForLog"/>.</summary>
        public static string BlobNameForLog(string blobName) => BlobNameLogFormatter.ForLog(blobName);

        /// <summary>Maps a listed <see cref="BlobItem"/> to a <see cref="BlobListItem"/> (name and creation time).</summary>
        public static BlobListItem ToBlobListItem(BlobItem blob) => new(blob.Name, blob.Properties?.CreatedOn);

        /// <inheritdoc/>
        public string GetBlobUrl(string containerName, string blobName)
        {
            // Validate input parameters
            if (string.IsNullOrEmpty(containerName))
            {
                throw new ArgumentException("Container name cannot be null or empty", nameof(containerName));
            }

            if (string.IsNullOrEmpty(blobName))
            {
                throw new ArgumentException("Blob name cannot be null or empty", nameof(blobName));
            }

            // Ensure base URL doesn't end with a slash
            var baseUrl = _storageBaseUrl.TrimEnd('/');

            // Construct and return full blob URL
            return $"{baseUrl}/{containerName}/{blobName}";
        }

        /// <inheritdoc/>
        public async Task<string> GenerateSasUrlAsync(Uri blobUrl, TimeSpan expiry)
        {
            // Parse container name and blob path from the plain blob URL.
            // URL structure: https://{account}.blob.core.windows.net/{container}/{blobPath}
            // Uri.Segments for the above: ["/", "{container}/", "{blobPath-segment1}/", "{blobPath-segment2}", ...]
            var segments = blobUrl.Segments;
            if (segments.Length < 3)
                throw new ArgumentException($"Cannot parse container and blob name from URL: {blobUrl}", nameof(blobUrl));

            var containerName = segments[1].TrimEnd('/');
            var blobPath = string.Concat(segments.Skip(2).Select(Uri.UnescapeDataString));

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);

            Uri sasUri;

            if (!blobClient.CanGenerateSasUri)
            {
                // Managed identity: use User Delegation SAS (requires Storage Blob Delegator role on the storage account)
                var startsOn = _dateTimeProvider.DateTimeOffsetUtcNow().AddMinutes(-5);
                var expiresOn = _dateTimeProvider.DateTimeOffsetUtcNow().Add(expiry);

                var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(startsOn, expiresOn, default);

                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = containerName,
                    BlobName = blobPath,
                    Resource = "b",
                    StartsOn = startsOn,
                    ExpiresOn = expiresOn,
                    Protocol = SasProtocol.Https
                };
                sasBuilder.SetPermissions(BlobSasPermissions.Read);

                var sasParams = sasBuilder.ToSasQueryParameters(userDelegationKey, _blobServiceClient.AccountName);
                sasUri = new UriBuilder(blobClient.Uri) { Query = sasParams.ToString() }.Uri;
            }
            else
            {
                // Account key mode: local computation, no network call
                sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, _dateTimeProvider.DateTimeOffsetUtcNow().Add(expiry));
            }

            if (!string.IsNullOrEmpty(_baseCdnUrl))
                return $"{_baseCdnUrl.TrimEnd('/')}{sasUri.PathAndQuery}";

            return sasUri.AbsoluteUri;
        }

        /// <summary>
        /// Resolves the correct <see cref="BlobServiceClient"/> based on available credentials:
        /// connection string → managed identity (MI_CLIENT_ID) → Visual Studio / Azure CLI.
        /// </summary>
        public static BlobServiceClient CreateBlobServiceClient(BlobStorageSettings settings)
        {
            return new BlobServiceClient(new Uri(settings.BaseUrl), AzureCredentialHelper.Create());
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, bool>> ExistsManyAsync(string containerName, IEnumerable<string> blobNames, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get container client
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // If container doesn't exist, none of the blobs exist
                if (!await containerClient.ExistsAsync(cancellationToken))
                {
                    return blobNames.ToDictionary(name => name, _ => false);
                }

                // Create a thread-safe dictionary to store results
                var results = new ConcurrentDictionary<string, bool>();

                // Check existence of each blob in parallel for better performance
                await Parallel.ForEachAsync(
                    blobNames,
                    new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = cancellationToken },
                    async (blobName, ct) =>
                    {
                        var blobClient = containerClient.GetBlobClient(blobName);
                        var exists = await blobClient.ExistsAsync(ct);
                        results[blobName] = exists;
                    });

                // Convert to regular dictionary for return
                return new Dictionary<string, bool>(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence of multiple blobs in container {ContainerName}", containerName);
                throw;
            }
        }
    }
}
