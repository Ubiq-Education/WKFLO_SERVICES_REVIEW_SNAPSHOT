using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.UpdateTenantKnowledgeDescription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Knowledge.Commands.UpdateTenantKnowledgeDescription
{
    public class UpdateTenantKnowledgeDescriptionCommandHandlerTesting
    {
        public const string TenantName = "Test";
        public const string ExistingDocId = "existing-doc-id";
        public const string MissingDocId = "missing-doc-id";

        public UpdateTenantKnowledgeDescriptionCommandHandler Handler { get; }
        public Mock<IDatastreamProvider> DatastreamProviderMock { get; }

        public UpdateTenantKnowledgeDescriptionCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };

            var elasticsearchSettings = Options.Create(new ElasticsearchSettings
            {
                KnowledgeIndex = "tenant-knowledge-index"
            });

            DatastreamProviderMock = new Mock<IDatastreamProvider>();

            DatastreamProviderMock
                .Setup(x => x.ExistDocumentAsync<KnowledgeDocument>(ExistingDocId, TenantName, elasticsearchSettings.Value.KnowledgeIndex))
                .ReturnsAsync(true);
            DatastreamProviderMock
                .Setup(x => x.ExistDocumentAsync<KnowledgeDocument>(MissingDocId, TenantName, elasticsearchSettings.Value.KnowledgeIndex))
                .ReturnsAsync(false);

            DatastreamProviderMock
                .Setup(x => x.UpdateDocumentDescriptionAsync(ExistingDocId, It.IsAny<string>(), TenantName, elasticsearchSettings.Value.KnowledgeIndex))
                .ReturnsAsync(true);

            // Elasticsearch's _search API is only near-real-time (default refresh_interval ~1s),
            // so the handler explicitly refreshes the index after a successful update to make the
            // new description immediately visible to the list endpoint.
            DatastreamProviderMock
                .Setup(x => x.RefreshIndexAsync(elasticsearchSettings.Value.KnowledgeIndex))
                .ReturnsAsync(true);

            Handler = new UpdateTenantKnowledgeDescriptionCommandHandler(
                Mock.Of<ILogger<UpdateTenantKnowledgeDescriptionCommandHandler>>(),
                DatastreamProviderMock.Object,
                elasticsearchSettings,
                tenantContext);
        }
    }
}
