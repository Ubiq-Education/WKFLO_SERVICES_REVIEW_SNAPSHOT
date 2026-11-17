using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.DeleteTenantKnowledge
{
    public class DeleteTenantKnowledgeCommandHandler : IRequestHandler<DeleteTenantKnowledgeCommand, Result>
    {
        // Same container the uploader writes to (TenantknowledgeUploader).
        public const string KnowledgeContainerName = "tenant-knowledge";

        private readonly ILogger<DeleteTenantKnowledgeCommandHandler> _logger;
        protected readonly IDatastreamProvider _datastreamProvider;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly ElasticsearchSettings _elasticsearchSettings;
        private readonly TenantContext _tenantContext;

        public DeleteTenantKnowledgeCommandHandler(
            ILogger<DeleteTenantKnowledgeCommandHandler> logger,
            IDatastreamProvider datastreamProvider,
            IBlobStorageProvider blobStorageProvider,
            IOptions<ElasticsearchSettings> settings,
            TenantContext tenantContext)
        {
            _logger = logger;
            _datastreamProvider = datastreamProvider;
            _blobStorageProvider = blobStorageProvider;
            _elasticsearchSettings = settings.Value;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(DeleteTenantKnowledgeCommand command, CancellationToken cancellationToken)
        {
            // Read the document first (tenant-scoped) so its uploaded file can be removed after the index entry is gone.
            var document = await _datastreamProvider.GetDocumentByIdAsync<BaseKnowledgeResult>(command.Id, _tenantContext.TenantName, _elasticsearchSettings.KnowledgeIndex);
            if (document == null)
            {
                // The validator has just found this document for the tenant, so null means the read failed (the provider returns
                // null on any Elasticsearch error). Deleting now would lose the only record of the file's URL, so fail and keep both.
                _logger.LogError("Knowledge document {DocumentId} could not be read before deleting it; nothing deleted", command.Id);
                return new UnexpectedResult();
            }

            var result = await _datastreamProvider.DeleteDocumentAsync(command.Id, _elasticsearchSettings.KnowledgeIndex);

            // Refresh the index to make the document immediately dissapear for search
            await _datastreamProvider.RefreshIndexAsync(_elasticsearchSettings.KnowledgeIndex);

            if (!result)
            {
                return new NotFoundResult("Knowledge", ErrorConst.KnowledgeNotFound);
            }

            // The index entry is gone, so the file is cleaned up even if the client has disconnected meanwhile.
            await DeleteUploadedFileAsync(command.Id, document.BlobUrl, CancellationToken.None);

            return new SuccessResult<TenantKnowledgeDeleteResult>(new TenantKnowledgeDeleteResult() { Id = command.Id });
        }

        /// <summary>
        /// Best effort: the knowledge document is already deleted, so a storage failure is logged and never fails the request.
        /// The blob name is rebuilt from the stored URL under the caller's tenant folder, as <c>TenantknowledgeDownloader</c> does,
        /// so a URL can never point the delete at another tenant's file.
        /// </summary>
        private async Task DeleteUploadedFileAsync(string documentId, string blobUrl, CancellationToken cancellationToken)
        {
            if (!TryGetBlobName(blobUrl, _tenantContext.TenantName, out var blobName))
            {
                _logger.LogWarning("Knowledge document {DocumentId} has no usable file URL; no file deleted", documentId);
                return;
            }

            try
            {
                var deleted = await _blobStorageProvider.DeleteAsync(KnowledgeContainerName, blobName, cancellationToken);
                if (!deleted)
                {
                    _logger.LogWarning("File for knowledge document {DocumentId} was not found in storage", documentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deleting the file for knowledge document {DocumentId} failed; the file stays in storage", documentId);
            }
        }

        internal static bool TryGetBlobName(string blobUrl, string tenantName, out string blobName)
        {
            blobName = null;
            if (string.IsNullOrWhiteSpace(tenantName) || !Uri.TryCreate(blobUrl, UriKind.Absolute, out var blobUri))
            {
                return false;
            }

            var fileName = Path.GetFileName(blobUri.LocalPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            blobName = $"{tenantName}/{fileName}";
            return true;
        }
    }
}
