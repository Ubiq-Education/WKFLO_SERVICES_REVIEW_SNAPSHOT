using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using Mediator;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.UploadTenantKnowledge
{
    public class UploadTenantKnowledgeCommandHandler : IRequestHandler<UploadTenantKnowledgeCommand, Result>
    {
        private readonly IDatastreamProvider _datastreamProvider;
        private readonly ElasticsearchSettings _elasticsearchSettings;
        private readonly TenantContext _tenantContext;
        private readonly IDateTimeProvider _dateTimeProvider;

        public UploadTenantKnowledgeCommandHandler(IDatastreamProvider datastreamProvider, IOptions<ElasticsearchSettings> settings, TenantContext tenantContext, IDateTimeProvider dateTimeProvider)
        {
            _datastreamProvider = datastreamProvider;
            _elasticsearchSettings = settings.Value;
            _tenantContext = tenantContext;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<Result> Handle(UploadTenantKnowledgeCommand command, CancellationToken cancellationToken)
        {
            try
            {
                var knowledgeDocument = new KnowledgeDocument
                {
                    FileName = command.FileName,
                    Title = command.Title,
                    Description = command.Description,
                    Size = command.Size,
                    ContentType = command.ContentType,
                    Base64Content = command.Base64Content,
                    TenantName = _tenantContext.TenantName,
                    Tags = command.Tags,
                    BlobUrl = command.BlobUrl,
                    Timestamp = _dateTimeProvider.DateTimeOffsetUtcNow()
                };

                await _datastreamProvider.PutAllKnowledgePipelineAsync("tenant-knowledge-pipeline");
                await _datastreamProvider.IndexAsync(knowledgeDocument, _elasticsearchSettings.KnowledgeIndex, "tenant-knowledge-pipeline");

                // Refresh the index to make the document immediately available for search
                await _datastreamProvider.RefreshIndexAsync(_elasticsearchSettings.KnowledgeIndex);

                return new SuccessResult<string>($"Indexed: {command.FileName}");
            }
            catch (Exception ex)
            {
                return new UnexpectedResult("Knowledge Upload", $"Error processing file: {ex.Message}");
            }
        }
    }
}
