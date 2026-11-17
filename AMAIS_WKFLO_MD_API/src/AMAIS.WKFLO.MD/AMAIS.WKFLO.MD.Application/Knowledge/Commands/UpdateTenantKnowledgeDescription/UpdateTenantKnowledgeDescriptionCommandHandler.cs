using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.UpdateTenantKnowledgeDescription
{
    public class UpdateTenantKnowledgeDescriptionCommandHandler : IRequestHandler<UpdateTenantKnowledgeDescriptionCommand, Result>
    {
        private readonly ILogger<UpdateTenantKnowledgeDescriptionCommandHandler> _logger;
        private readonly IDatastreamProvider _datastreamProvider;
        private readonly ElasticsearchSettings _elasticsearchSettings;
        private readonly TenantContext _tenantContext;

        public UpdateTenantKnowledgeDescriptionCommandHandler(ILogger<UpdateTenantKnowledgeDescriptionCommandHandler> logger, IDatastreamProvider datastreamProvider, IOptions<ElasticsearchSettings> settings, TenantContext tenantContext)
        {
            _logger = logger;
            _datastreamProvider = datastreamProvider;
            _elasticsearchSettings = settings.Value;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(UpdateTenantKnowledgeDescriptionCommand command, CancellationToken cancellationToken)
        {
            try
            {
                // Check if document exists in Elasticsearch and belongs to current tenant
                var documentExists = await _datastreamProvider.ExistDocumentAsync<KnowledgeDocument>(
                    command.DocId,
                    _tenantContext.TenantName,
                    _elasticsearchSettings.KnowledgeIndex);

                if (!documentExists)
                {
                    return new NotFoundResult();
                }

                // Update document in Elasticsearch with new description
                var updateSuccessful = await _datastreamProvider.UpdateDocumentDescriptionAsync(command.DocId, command.Description, _tenantContext.TenantName, _elasticsearchSettings.KnowledgeIndex);

                if (!updateSuccessful)
                {
                    return new UnexpectedResult(nameof(command.Description), "Failed to update document description");
                }

                // Refresh the index so the new description is immediately visible to the list
                // endpoint's search (Elasticsearch's default refresh_interval is ~1s, so without
                // this a page reload right after saving can still return the stale value).
                // The description was already persisted above, so a refresh failure here is
                // logged but does not fail the overall operation.
                try
                {
                    await _datastreamProvider.RefreshIndexAsync(_elasticsearchSettings.KnowledgeIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh index after updating description for document {DocumentId}", command.DocId);
                }

                return new SuccessResult<bool>(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating description for document {DocumentId}", command.DocId);
                return new UnexpectedResult(nameof(command.Description), $"Error updating document description: {ex.Message}");
            }
        }
    }
}
