using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.UpdateTenantKnowledgeTags
{
    public class UpdateTenantKnowledgeTagsCommandHandler : IRequestHandler<UpdateTenantKnowledgeTagsCommand, Result>
    {
        private readonly ILogger<UpdateTenantKnowledgeTagsCommandHandler> _logger;
        private readonly IDatastreamProvider _datastreamProvider;
        private readonly ElasticsearchSettings _elasticsearchSettings;
        private readonly TenantContext _tenantContext;

        public UpdateTenantKnowledgeTagsCommandHandler(ILogger<UpdateTenantKnowledgeTagsCommandHandler> logger, IDatastreamProvider datastreamProvider, IOptions<ElasticsearchSettings> settings, TenantContext tenantContext)
        {
            _logger = logger;
            _datastreamProvider = datastreamProvider;
            _elasticsearchSettings = settings.Value;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(UpdateTenantKnowledgeTagsCommand command, CancellationToken cancellationToken)
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

                // Update document in Elasticsearch with new tags
                var updateSuccessful = await _datastreamProvider.UpdateDocumentTagsAsync(command.DocId, command.Tags, _tenantContext.TenantName, _elasticsearchSettings.KnowledgeIndex);

                if (!updateSuccessful)
                {
                    return new UnexpectedResult(nameof(command.Tags), "Failed to update document tags");
                }

                return new SuccessResult<bool>(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating tags for document {DocumentId}", command.DocId);
                return new UnexpectedResult(nameof(command.Tags), $"Error updating document tags: {ex.Message}");
            }
        }
    }
}
