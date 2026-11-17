using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using AMAIS.WKFLO.MD.Application.Knowledge.Queries.ListTenantKnowledge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.knowledge
{
    public class TenantknowledgeDownloader : BaseAsyncEndpoint<string, Result>
    {
        private readonly ILogger<TenantknowledgeDownloader> _logger;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly TenantContext _tenantContext;
        private readonly IDatastreamProvider _datastreamProvider;
        private readonly ElasticsearchSettings _elasticsearchSettings;

        public TenantknowledgeDownloader(
            ILogger<TenantknowledgeDownloader> logger,
            IOptions<ElasticsearchSettings> elasticsearchSettings,
            IWkfloMediator mediator,
            IBlobStorageProvider blobStorageProvider,
            TenantContext tenantContext,
            IDatastreamProvider datastreamProvider) : base(mediator)
        {
            _logger = logger;
            _elasticsearchSettings = elasticsearchSettings.Value;
            _blobStorageProvider = blobStorageProvider;
            _tenantContext = tenantContext;
            _datastreamProvider = datastreamProvider;
        }

        [Authorize]
        [HttpGet("/api/v1/tenant/knowledge/download/{documentId}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Knowledge")]
        [EndpointName("TenantKnowledge.Download")]
        [EndpointSummary("Download a specific knowledge document")]
        [EndpointDescription("Downloads a knowledge document by its ID")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(string documentId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate input parameter
                if (string.IsNullOrEmpty(documentId))
                {
                    return AmaisWkfloResult(new InvalidResult("Downloader", "Document ID is required"));
                }

                // Tenant is resolved from the amais-wkflo-tenant-guid / amais-wkflo-tenant-name headers; without it
                // the tenant-scoped Elasticsearch lookup below would send a null term and fail with a parse error.
                if (string.IsNullOrWhiteSpace(_tenantContext.TenantName))
                {
                    return AmaisWkfloResult(new InvalidResult("Downloader", "Tenant is required"));
                }

                // Check if document exists in Elasticsearch and belongs to current tenant
                var documentExists = await _datastreamProvider.ExistDocumentAsync<Common.Models.Results.Knowledge.KnowledgeDocument>(
                    documentId,
                    _tenantContext.TenantName,
                    _elasticsearchSettings.KnowledgeIndex);

                if (!documentExists)
                {
                    return AmaisWkfloResult(new Common.Models.Results.NotFoundResult("Document", $"Document with ID {documentId} not found or you don't have permission to access it"));
                }

                // Retrieve all tenant knowledge documents to find the one we need
                var knowledgeQuery = new ListTenantKnowledgeQuery();
                var knowledgeResult = await Mediator.Send(knowledgeQuery, cancellationToken);

                // Set container name - must match the one used during upload
                string containerName = "tenant-knowledge";

                // Construct blob name with tenant prefix for isolation
                string blobName = $"{_tenantContext.TenantName}/{documentId}";

                // Check if blob exists with this name
                bool exists = await _blobStorageProvider.ExistsAsync(containerName, blobName, cancellationToken);

                if (!exists)
                {
                    // If blob doesn't exist with direct ID, try to find it using metadata from Elasticsearch
                    // This handles cases where document ID differs from blob name
                    if (knowledgeResult is SuccessResult<List<TenantKnowledgeGetResult>> success)
                    {
                        var document = success.Data.FirstOrDefault(d => d.Id == documentId);
                        if (document != null && !string.IsNullOrEmpty(document.BlobUrl))
                        {
                            // Extract blob name from URL and maintain tenant isolation
                            Uri blobUri = new Uri(document.BlobUrl);
                            blobName = $"{_tenantContext.TenantName}/{Path.GetFileName(blobUri.LocalPath)}";

                            // Verify again with extracted name
                            exists = await _blobStorageProvider.ExistsAsync(containerName, blobName, cancellationToken);
                            if (!exists)
                            {
                                return AmaisWkfloResult(new Common.Models.Results.NotFoundResult("Document", $"Document file not found in storage"));
                            }
                        }
                        else
                        {
                            return AmaisWkfloResult(new Common.Models.Results.NotFoundResult("Document", $"Document with ID {documentId} not found or has no associated file"));
                        }
                    }
                    else
                    {
                        return AmaisWkfloResult(new Common.Models.Results.NotFoundResult("Document", $"Document with ID {documentId} not found"));
                    }
                }

                // Download file content as stream
                var fileStream = await _blobStorageProvider.DownloadAsync(containerName, blobName, cancellationToken);
                if (fileStream == null)
                {
                    return AmaisWkfloResult(new UnexpectedResult("Downloader", "Error downloading the file"));
                }

                // Convert stream to byte array for file response
                byte[] fileBytes;
                using (var memoryStream = new MemoryStream())
                {
                    await fileStream.CopyToAsync(memoryStream, cancellationToken);
                    fileBytes = memoryStream.ToArray();
                }

                // Set default filename and content type
                string fileName = blobName;
                string contentType = "application/octet-stream"; // Default content type if not specified

                // Try to get original filename and content type from document metadata
                if (knowledgeResult is SuccessResult<List<TenantKnowledgeGetResult>> successResult)
                {
                    var document = successResult.Data.FirstOrDefault(d => d.Id == documentId);
                    if (document != null)
                    {
                        fileName = document.FileName ?? fileName;
                        contentType = document.ContentType ?? contentType;
                    }
                }

                // Return file with appropriate content type and filename
                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading document with ID {DocumentId}", documentId);
                return AmaisWkfloResult(new UnexpectedResult("Downloader", $"Error downloading file: {ex.Message}"));
            }
        }
    }
}
